using System;
using System.Collections.Generic;
using System.Net;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.Hardcover;

namespace NzbDrone.Core.MetadataSource.Goodreads
{
    public interface IGoodreadsSearchProxy
    {
        public List<SearchJsonResource> Search(string query);
        public List<SearchJsonResource> SearchMetadataApi(string query);
    }

    public class GoodreadsSearchProxy : IGoodreadsSearchProxy
    {
        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly IMetadataRequestBuilder _metadataRequestBuilder;
        private readonly IHardcoverMetadataProxy _hardcoverMetadataProxy;
        private readonly Logger _logger;

        public GoodreadsSearchProxy(ICachedHttpResponseService cachedHttpClient,
            IMetadataRequestBuilder metadataRequestBuilder,
            IHardcoverMetadataProxy hardcoverMetadataProxy,
            Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _metadataRequestBuilder = metadataRequestBuilder;
            _hardcoverMetadataProxy = hardcoverMetadataProxy;
            _logger = logger;
        }

        public List<SearchJsonResource> Search(string query)
        {
            if (_hardcoverMetadataProxy.IsNativeEnabled)
            {
                return _hardcoverMetadataProxy.Search(query);
            }

            return SearchMetadataApi(query);
        }

        public List<SearchJsonResource> SearchMetadataApi(string query)
        {
            if (query == null)
            {
                return new List<SearchJsonResource>();
            }

            try
            {
                var httpRequest = _metadataRequestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", "search")
                    .AddQueryParam("q", query)
                    .Build();

                var response = _cachedHttpClient.Get<List<SearchJsonResource>>(httpRequest, false, TimeSpan.FromDays(5));

                return response.Resource;
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new GoodreadsException("Search for '{0}' failed. Unable to communicate with metadata source.", ex, query);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new GoodreadsException("Search for '{0}' failed. Unable to communicate with metadata source.", ex, query, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new GoodreadsException("Search for '{0}' failed. Invalid response received from metadata source.", ex, query);
            }
        }
    }
}
