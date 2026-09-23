using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace Bookshelf.Diagnostics
{
    public interface IDiagnosticsReporter
    {
        void Start();
        void Record(string eventName);
        Task StopAsync();
    }

    public sealed class DiagnosticsReporter : IDiagnosticsReporter, IDisposable
    {
        private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly object _sync = new object();
        private readonly SemaphoreSlim _flushLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, long> _counts = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Logger _logger;
        private readonly Uri _endpoint;
        private readonly string _token;
        private readonly HttpClient _httpClient;
        private Timer _timer;
        private DateTimeOffset _windowStart;
        private bool _started;
        private bool _stopped;

        public DiagnosticsReporter(Logger logger)
        {
            _logger = logger;

            if (!TryReadConfiguration(out _endpoint, out _token))
            {
                _logger.Warn("Optional diagnostics module is inactive because its HTTPS OTLP endpoint or bearer token is missing or invalid.");
                return;
            }

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = RequestTimeout
            };
        }

        public void Start()
        {
            if (_httpClient == null)
            {
                return;
            }

            lock (_sync)
            {
                if (_started || _stopped)
                {
                    return;
                }

                _started = true;
                _windowStart = DateTimeOffset.UtcNow;
                _timer = new Timer(OnTimer, null, ReportInterval, ReportInterval);
            }
        }

        public void Record(string eventName)
        {
            if (_httpClient == null)
            {
                return;
            }

            lock (_sync)
            {
                if (!_started || _stopped)
                {
                    return;
                }

                _counts.TryGetValue(eventName, out var count);
                _counts[eventName] = count + 1;
            }
        }

        public async Task StopAsync()
        {
            lock (_sync)
            {
                if (_stopped)
                {
                    return;
                }

                _stopped = true;
                _timer?.Dispose();
                _timer = null;
            }

            if (_httpClient != null)
            {
                using (var cancellation = new CancellationTokenSource(RequestTimeout))
                {
                    await FlushAsync(cancellation.Token).ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _httpClient?.Dispose();
            _flushLock.Dispose();
        }

        private static bool TryReadConfiguration(out Uri endpoint, out string token)
        {
            endpoint = null;
            token = Environment.GetEnvironmentVariable("BOOKSHELF_DIAGNOSTICS_AUTH_TOKEN");
            var endpointValue = Environment.GetEnvironmentVariable("BOOKSHELF_DIAGNOSTICS_OTLP_ENDPOINT");

            if (string.IsNullOrWhiteSpace(endpointValue) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var parsed) ||
                parsed.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(parsed.UserInfo) ||
                !string.IsNullOrEmpty(parsed.Query) ||
                !string.IsNullOrEmpty(parsed.Fragment) ||
                !parsed.AbsolutePath.TrimEnd('/').EndsWith("/v1/metrics", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            endpoint = parsed;
            token = token.Trim();
            return token.Length > 0;
        }

        private void OnTimer(object state)
        {
            _ = FlushAsync(CancellationToken.None);
        }

        private async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_httpClient == null || !await _flushLock.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }

            Dictionary<string, long> snapshot;
            var windowStart = DateTimeOffset.UtcNow;
            var windowEnd = DateTimeOffset.UtcNow;

            try
            {
                lock (_sync)
                {
                    if (_counts.Count == 0)
                    {
                        return;
                    }

                    snapshot = new Dictionary<string, long>(_counts, StringComparer.Ordinal);
                    windowStart = _windowStart;
                }

                var payload = CreatePayload(snapshot, windowStart, windowEnd);
                using (var request = new HttpRequestMessage(HttpMethod.Post, _endpoint))
                using (var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"))
                using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                    request.Content = content;

                    using (var response = await _httpClient.SendAsync(request, cancellation.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Warn("Optional diagnostics collector returned HTTP {0}; cumulative counters remain available for retry.", (int)response.StatusCode);
                        }
                    }
                }
            }
            catch (Exception)
            {
                _logger.Warn("Optional diagnostics report failed; cumulative counters remain available for retry.");
            }
            finally
            {
                _flushLock.Release();
            }
        }

        private object CreatePayload(Dictionary<string, long> counts, DateTimeOffset start, DateTimeOffset end)
        {
            var dataPoints = new List<object>();
            foreach (var entry in counts)
            {
                dataPoints.Add(new
                {
                    attributes = new[]
                    {
                        new { key = "event", value = new { stringValue = entry.Key } }
                    },
                    startTimeUnixNano = ToUnixNanoseconds(start),
                    timeUnixNano = ToUnixNanoseconds(end),
                    asInt = entry.Value.ToString(CultureInfo.InvariantCulture)
                });
            }

            return new
            {
                resourceMetrics = new[]
                {
                    new
                    {
                        resource = new
                        {
                            attributes = new[]
                            {
                                new { key = "service.name", value = new { stringValue = "bookshelfng" } },
                                new { key = "service.version", value = new { stringValue = BuildInfo.Version.ToString() } }
                            }
                        },
                        scopeMetrics = new[]
                        {
                            new
                            {
                                scope = new { name = "bookshelfng.optional-diagnostics" },
                                metrics = new[]
                                {
                                    new
                                    {
                                        name = "bookshelfng.events",
                                        unit = "{event}",
                                        sum = new
                                        {
                                            dataPoints,
                                            aggregationTemporality = 2,
                                            isMonotonic = true
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static string ToUnixNanoseconds(DateTimeOffset value)
        {
            return (value.ToUnixTimeMilliseconds() * 1000000L).ToString(CultureInfo.InvariantCulture);
        }
    }
}
