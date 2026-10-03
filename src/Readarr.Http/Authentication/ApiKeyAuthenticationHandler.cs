using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NzbDrone.Core.Configuration;

namespace Readarr.Http.Authentication
{
    public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string DefaultScheme = "API Key";

        public string Scheme => DefaultScheme;
        public string AuthenticationType = DefaultScheme;

        public string HeaderName { get; set; }
        public string QueryName { get; set; }
        public bool AllowQueryString { get; set; }
    }

    public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        private readonly string _apiKey;
        private readonly string _seerrIntegrationKey;

        public ApiKeyAuthenticationHandler(IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfigFileProvider config,
            IConfiguration configuration)
            : base(options, logger, encoder)
        {
            _apiKey = config.ApiKey;
            _seerrIntegrationKey = configuration["BOOKSHELF_SEERRNG_API_KEY"];
        }

        private string ParseApiKey()
        {
            // Query-string credentials are disabled by default because URLs are
            // commonly retained in browser history, logs, and proxy access logs.
            if (Options.AllowQueryString && !string.IsNullOrWhiteSpace(Options.QueryName) &&
                Request.Query.TryGetValue(Options.QueryName, out var value))
            {
                return value.FirstOrDefault();
            }

            // No ApiKey query parameter found try headers
            if (Request.Headers.TryGetValue(Options.HeaderName, out var headerValue))
            {
                return headerValue.FirstOrDefault();
            }

            return Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "");
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var providedApiKey = ParseApiKey();

            if (string.IsNullOrWhiteSpace(providedApiKey))
            {
                return AuthenticateResult.NoResult();
            }

            if (MatchesKey(_apiKey, providedApiKey))
            {
                var claims = new List<Claim>
                {
                    new Claim("ApiKey", "true")
                };

                var identity = new ClaimsIdentity(claims, Options.AuthenticationType);
                var identities = new List<ClaimsIdentity> { identity };
                var principal = new ClaimsPrincipal(identities);
                var ticket = new AuthenticationTicket(principal, Options.Scheme);

                return AuthenticateResult.Success(ticket);
            }

            if (!string.IsNullOrWhiteSpace(_seerrIntegrationKey) &&
                !MatchesKey(_apiKey, _seerrIntegrationKey) &&
                MatchesKey(_seerrIntegrationKey, providedApiKey) &&
                await IsAllowedSeerrIntegrationRequestAsync())
            {
                var claims = new List<Claim>
                {
                    new Claim("IntegrationApiKey", "SeerrNG"),
                    new Claim("MediaType", GetSeerrMediaType())
                };

                var identity = new ClaimsIdentity(claims, Options.AuthenticationType);
                var principal = new ClaimsPrincipal(new List<ClaimsIdentity> { identity });
                var ticket = new AuthenticationTicket(principal, Options.Scheme);

                return AuthenticateResult.Success(ticket);
            }

            return AuthenticateResult.NoResult();
        }

        private static bool MatchesKey(string expected, string provided)
        {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
            {
                return false;
            }

            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
            var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
            return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
        }

        private string GetSeerrMediaType()
        {
            if (Context.Items.TryGetValue(SeerrIntegrationContext.MediaTypeItem, out var mediaType))
            {
                return mediaType?.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
            }

            var queryValues = Request.Query["mediaType"];
            return queryValues.Count == 1
                ? queryValues[0]?.Trim().ToLowerInvariant() ?? string.Empty
                : string.Empty;
        }

        private async Task<bool> IsAllowedSeerrIntegrationRequestAsync()
        {
            var path = (Request.Path.Value?.TrimEnd('/') ?? string.Empty).ToLowerInvariant();
            var method = Request.Method;
            var mediaType = GetSeerrMediaType();

            if (mediaType != "ebook" && mediaType != "audiobook")
            {
                return false;
            }

            if (method == "GET")
            {
                return path == "/api/v1/system/status" ||
                    path == "/api/v1/system/capabilities" ||
                    path == "/api/v1/author" ||
                    path == "/api/v1/author/paged" ||
                    path == "/api/v1/author/lookup" ||
                    IsResourcePath(path, "/api/v1/author/") ||
                    path == "/api/v1/book" ||
                    path == "/api/v1/book/paged" ||
                    path == "/api/v1/book/lookup" ||
                    IsResourcePath(path, "/api/v1/book/") ||
                    path == "/api/v1/rootfolder" ||
                    path == "/api/v1/qualityprofile" ||
                    path == "/api/v1/metadataprofile" ||
                    path == "/api/v1/tag" ||
                    path == "/api/v1/queue" ||
                    (path == "/api/v1/history" && HasSinglePositiveIdQuery("bookId")) ||
                    (path == "/api/v1/bookfile" && HasSinglePositiveIdQuery("bookId")) ||
                    IsResourcePath(path, "/api/v1/command/");
            }

            if (method == "POST")
            {
                if (path == "/api/v1/author" || path == "/api/v1/book")
                {
                    return true;
                }

                return path == "/api/v1/command" && await IsAllowedSeerrBookSearchCommandAsync();
            }

            if (method == "PUT")
            {
                return IsResourcePath(path, "/api/v1/author/") || IsResourcePath(path, "/api/v1/book/");
            }

            return false;
        }

        private bool HasSinglePositiveIdQuery(string name)
        {
            var values = Request.Query[name];
            return values.Count == 1 && int.TryParse(values[0], out var id) && id > 0;
        }

        private async Task<bool> IsAllowedSeerrBookSearchCommandAsync()
        {
            if (!Request.HasJsonContentType() || Request.ContentLength > 4096)
            {
                return false;
            }

            try
            {
                Request.EnableBuffering(bufferThreshold: 4096, bufferLimit: 4096);
                using var document = await JsonDocument.ParseAsync(
                    Request.Body,
                    new JsonDocumentOptions { MaxDepth = 8 },
                    Context.RequestAborted);

                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                string commandName = null;
                var commandNameCount = 0;
                var bookIdCount = 0;
                var bookIdIsValid = false;
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name.Equals("name", System.StringComparison.OrdinalIgnoreCase))
                    {
                        commandNameCount++;
                        commandName = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : null;
                    }
                    else if (property.Name.Equals("bookIds", System.StringComparison.OrdinalIgnoreCase))
                    {
                        bookIdCount++;
                        if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() == 1)
                        {
                            var bookId = property.Value[0];
                            bookIdIsValid = bookId.ValueKind == JsonValueKind.Number &&
                                bookId.TryGetInt32(out var parsedBookId) && parsedBookId > 0;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                return string.Equals(commandName, "BookSearch", System.StringComparison.Ordinal) &&
                    commandNameCount == 1 && bookIdCount == 1 && bookIdIsValid;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            finally
            {
                if (Request.Body.CanSeek)
                {
                    Request.Body.Position = 0;
                }
            }
        }

        private static bool IsResourcePath(string path, string prefix)
        {
            if (!path.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                return false;
            }

            var id = path.Substring(prefix.Length);
            return id.IndexOf('/') < 0 && int.TryParse(id, out var parsedId) && parsedId > 0;
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 401;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 403;
            return Task.CompletedTask;
        }
    }
}
