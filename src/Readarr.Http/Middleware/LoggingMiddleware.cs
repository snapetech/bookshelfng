using System;
using System.Globalization;
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

            _loggerHttp.Trace("Res: {0} [{1}] {2}: {3}.{4} ({5} ms)", id, method, reqPath, context.Response.StatusCode, (HttpStatusCode)context.Response.StatusCode, (int)duration.TotalMilliseconds);

            if (context.Request.IsApiRequest())
            {
                _loggerApi.Debug("[{0}] {1}: {2}.{3} ({4} ms)", method, reqPath, context.Response.StatusCode, (HttpStatusCode)context.Response.StatusCode, (int)duration.TotalMilliseconds);
            }
        }

        private static string GetRequestPathAndQuery(HttpRequest request)
        {
            if (request.QueryString.Value.IsNotNullOrWhiteSpace() && request.QueryString.Value != "?")
            {
                return string.Concat(request.Path, request.QueryString);
            }
            else
            {
                return request.Path;
            }
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
