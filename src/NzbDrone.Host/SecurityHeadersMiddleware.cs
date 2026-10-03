using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace NzbDrone.Host
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "SAMEORIGIN";
                headers["Content-Security-Policy"] = "frame-ancestors 'self'";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

                if (context.Request.IsHttps)
                {
                    headers["Strict-Transport-Security"] = "max-age=31536000";
                }

                if (context.Request.Path.Value == "/initialize.json")
                {
                    headers["Cache-Control"] = "no-store, max-age=0";
                    headers["Pragma"] = "no-cache";
                }

                return Task.CompletedTask;
            });

            return _next(context);
        }
    }
}
