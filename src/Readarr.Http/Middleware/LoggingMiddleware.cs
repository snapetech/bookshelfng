using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Common.Extensions;
using Readarr.Http.ErrorManagement;
using Readarr.Http.Extensions;

namespace Readarr.Http.Middleware
{
    public class LoggingMiddleware
    {
        private static readonly Logger _loggerHttp = LogManager.GetLogger("Http");
        private static readonly Logger _loggerApi = LogManager.GetLogger("Api");
        private static readonly HashSet<string> _redactedQueryParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "apikey", "xapikey", "authorization", "auth", "key", "token", "accesstoken", "refreshtoken",
            "password", "passwd", "secret", "clientsecret", "code", "state", "q", "query", "term",
            "search", "title", "author", "session", "sessionid", "cookie", "jwt", "signature", "sig",
            "credential", "credentials", "redirect", "redirecturi", "returnurl", "url", "uri"
        };
        private static int _requestSequenceID;

        private readonly ReadarrErrorPipeline _errorHandler;
        private readonly RequestDelegate _next;

        public LoggingMiddleware(RequestDelegate next,
            ReadarrErrorPipeline errorHandler)
        {
            _next = next;
            _errorHandler = errorHandler;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            LogStart(context);

            await _next(context);

            LogEnd(context);
        }

        private void LogStart(HttpContext context)
        {
            var id = Interlocked.Increment(ref _requestSequenceID);

            context.Items["ApiRequestSequenceID"] = id;
            context.Items["ApiRequestStartTime"] = DateTime.UtcNow;
            context.Response.Headers["X-Bookshelf-Request-Id"] = id.ToString(CultureInfo.InvariantCulture);

            var method = SanitizeLogValue(context.Request.Method)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);
            var reqPath = SanitizeLogValue(GetRequestPathAndQuery(context.Request))
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);
            var origin = SanitizeLogValue(GetOrigin(context))
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);

            _loggerHttp.Trace("Req: {0} [{1}] {2} (from {3})", id, method, reqPath, origin);
        }

        private void LogEnd(HttpContext context)
        {
            var id = (int)context.Items["ApiRequestSequenceID"];
            var startTime = (DateTime)context.Items["ApiRequestStartTime"];

            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;

            var method = SanitizeLogValue(context.Request.Method)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);
            var reqPath = SanitizeLogValue(GetRequestPathAndQuery(context.Request))
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);

            var statusCode = context.Response.StatusCode;
            var statusDescription = (HttpStatusCode)statusCode;
            var host = SanitizeLogValue(context.Request.Host.Value);
            var scheme = SanitizeLogValue(context.Request.Scheme);
            var contentType = SanitizeLogValue(context.Response.ContentType ?? "unknown");
            var client = SanitizeLogValue(context.Request.Headers["User-Agent"].ToString());
            if (client.IsNullOrWhiteSpace())
            {
                client = "unknown";
            }

            _loggerHttp.Trace("Res: {0} [{1}] {2}: {3}.{4} ({5} ms)", id, method, reqPath, statusCode, statusDescription, (int)duration.TotalMilliseconds);

            if (context.Request.IsApiRequest())
            {
                _loggerApi.Debug("[{0}] [{1}] {2}: {3}.{4} ({5} ms); host={6}; scheme={7}; responseType={8}; client={9}", id, method, reqPath, statusCode, statusDescription, (int)duration.TotalMilliseconds, host, scheme, contentType, client);
            }
            else if (statusCode >= 300)
            {
                _loggerHttp.Debug("HTTP response [{0}] [{1}] {2}: {3}.{4} ({5} ms); host={6}; scheme={7}; responseType={8}; client={9}", id, method, reqPath, statusCode, statusDescription, (int)duration.TotalMilliseconds, host, scheme, contentType, client);
            }
        }

        private static string GetRequestPathAndQuery(HttpRequest request)
        {
            var query = QueryString.Create(request.Query.SelectMany(parameter => parameter.Value.Select(value =>
                new KeyValuePair<string, string>(parameter.Key, IsSensitiveQueryParameter(parameter.Key) ? "[redacted]" : value))));

            return string.Concat(request.PathBase, request.Path, query);
        }

        private static bool IsSensitiveQueryParameter(string parameterName)
        {
            var normalizedName = parameterName.Replace("-", string.Empty).Replace("_", string.Empty);

            return _redactedQueryParameters.Contains(normalizedName) ||
                   normalizedName.IndexOf("apikey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedName.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedName.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedName.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedName.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedName.IndexOf("query", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedName.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedName.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedName.IndexOf("author", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetOrigin(HttpContext context)
        {
            if (context.Request.Headers["User-Agent"].ToString().IsNullOrWhiteSpace())
            {
                return context.GetRemoteIP();
            }
            else
            {
                return $"{context.GetRemoteIP()} {context.Request.Headers["User-Agent"]}";
            }
        }

        private static string SanitizeLogValue(string value)
        {
            var sanitized = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (char.IsControl(character) ||
                    category == UnicodeCategory.Format ||
                    category == UnicodeCategory.LineSeparator ||
                    category == UnicodeCategory.ParagraphSeparator)
                {
                    sanitized.Append(' ');
                }
                else
                {
                    sanitized.Append(character);
                }
            }

            return sanitized.ToString();
        }
    }
}
