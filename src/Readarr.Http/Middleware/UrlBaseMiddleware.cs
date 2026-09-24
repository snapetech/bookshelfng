using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NzbDrone.Common.Extensions;

namespace Readarr.Http.Middleware
{
    public class UrlBaseMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _urlBase;

        public UrlBaseMiddleware(RequestDelegate next, string urlBase)
        {
            _next = next;
            _urlBase = urlBase;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (_urlBase.IsNotNullOrWhiteSpace() && context.Request.PathBase.Value.IsNullOrWhiteSpace())
            {
                var redirectUrl = $"{_urlBase}{context.Request.Path}{context.Request.QueryString}";

                if (!Uri.TryCreate(redirectUrl, UriKind.Relative, out var relativeUrl) ||
                    !redirectUrl.StartsWith("/", StringComparison.Ordinal) ||
                    redirectUrl.StartsWith("//", StringComparison.Ordinal) ||
                    redirectUrl.StartsWith("/\\", StringComparison.Ordinal))
                {
                    await _next(context);
                    return;
                }

                await Results.LocalRedirect(relativeUrl.ToString(), preserveMethod: true).ExecuteAsync(context);

                return;
            }

            await _next(context);
        }
    }
}
