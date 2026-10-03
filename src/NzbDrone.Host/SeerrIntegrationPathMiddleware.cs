using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Readarr.Http;

namespace NzbDrone.Host
{
    public class SeerrIntegrationPathMiddleware
    {
        private const string FacadePrefix = "/readarr/";
        private readonly RequestDelegate _next;

        public SeerrIntegrationPathMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith(FacadePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return _next(context);
            }

            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 ||
                !parts[0].Equals("readarr", StringComparison.OrdinalIgnoreCase) ||
                (!parts[1].Equals("gr", StringComparison.OrdinalIgnoreCase) &&
                 !parts[1].Equals("hc", StringComparison.OrdinalIgnoreCase)) ||
                !parts[3].Equals("api", StringComparison.OrdinalIgnoreCase) ||
                !parts[4].Equals("v1", StringComparison.OrdinalIgnoreCase) ||
                (!parts[2].Equals("ebook", StringComparison.OrdinalIgnoreCase) &&
                 !parts[2].Equals("audiobook", StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            var mediaType = parts[2].ToLowerInvariant();
            context.Items[SeerrIntegrationContext.MediaTypeItem] = mediaType;
            var resourcePath = parts.Length > 5 ? "/" + string.Join("/", parts, 5, parts.Length - 5) : string.Empty;
            context.Request.Path = new PathString("/api/v1" + resourcePath);

            return _next(context);
        }
    }
}
