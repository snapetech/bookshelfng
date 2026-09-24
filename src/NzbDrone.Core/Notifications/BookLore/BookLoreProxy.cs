using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using FluentValidation.Results;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.Notifications.BookLore
{
    public interface IBookLoreProxy
    {
        ValidationFailure Test(BookLoreSettings settings);
        void UploadFiles(BookLoreSettings settings, IEnumerable<string> filePaths);
    }

    public class BookLoreProxy : IBookLoreProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public BookLoreProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public ValidationFailure Test(BookLoreSettings settings)
        {
            try
            {
                GetAccessToken(settings);
                return null;
            }
            catch (HttpException ex) when (ex.Response.StatusCode == HttpStatusCode.Unauthorized || ex.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.Warn(ex, "BookLore rejected the configured credentials at {0}", settings.BaseUrl);
                return new ValidationFailure(nameof(settings.Password), "BookLore rejected the username or password");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to connect to BookLore at {0}", settings.BaseUrl);
                return new ValidationFailure(string.Empty, "Unable to connect to BookLore. Check the URL, credentials, and logs.");
            }
        }

        public void UploadFiles(BookLoreSettings settings, IEnumerable<string> filePaths)
        {
            var paths = filePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count == 0)
            {
                return;
            }

            var accessToken = GetAccessToken(settings);

            foreach (var path in paths)
            {
                UploadFile(settings, accessToken, path);
            }
        }

        private string GetAccessToken(BookLoreSettings settings)
        {
            var request = new HttpRequestBuilder($"{settings.BaseUrl.TrimEnd('/')}/api/v1/auth/login")
                .Post()
                .SetHeader("Content-Type", "application/json")
                .Accept(HttpAccept.Json)
                .Build();

            request.SetContent(JsonConvert.SerializeObject(new
            {
                username = settings.Username,
                password = settings.Password
            }));
            request.RequestTimeout = TimeSpan.FromSeconds(30);
            request.AllowAutoRedirect = false;

            var response = _httpClient.Execute(request);
            var accessToken = JObject.Parse(response.Content)["accessToken"]?.Value<string>();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidDataException("BookLore login response did not include an access token.");
            }

            return accessToken;
        }

        private void UploadFile(BookLoreSettings settings, string accessToken, string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The imported book file no longer exists.", path);
            }

            using var fileStream = File.OpenRead(path);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var formContent = new MultipartFormDataContent();
            formContent.Add(fileContent, "file", Path.GetFileName(path));

            var request = new HttpRequestBuilder($"{settings.BaseUrl.TrimEnd('/')}/api/v1/files/upload/bookdrop")
                .Post()
                .SetHeader("Authorization", $"Bearer {accessToken}")
                .KeepAlive()
                .Build();

            request.AllowAutoRedirect = false;
            request.RequestTimeout = TimeSpan.FromHours(1);
            request.ContentSummary = $"file={Path.GetFileName(path)} ({fileStream.Length} bytes)";
            request.SetContent(formContent);

            _httpClient.Execute(request);
            _logger.Info("Uploaded {0} to BookLore BookDrop", Path.GetFileName(path));
        }
    }
}
