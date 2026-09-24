using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;

namespace Readarr.Api.V1.Config
{
    [V1ApiController("config/metadataprovider")]
    public class MetadataProviderConfigController : ConfigController<MetadataProviderConfigResource>
    {
        public MetadataProviderConfigController(IConfigService configService)
            : base(configService)
        {
        }

        protected override MetadataProviderConfigResource ToResource(IConfigService model)
        {
            return MetadataProviderConfigResourceMapper.ToResource(model);
        }

        [RestPutById]
        public override ActionResult<MetadataProviderConfigResource> SaveConfig(MetadataProviderConfigResource resource)
        {
            var sourceValues = new[]
            {
                resource.EnableGoogleBooks,
                resource.EnableLoc,
                resource.EnableGutendex,
                resource.EnableInternetArchive,
                resource.EnableNdl,
                resource.EnableEuropeana,
                resource.EnableApifyGoodreads
            };
            var sources = new List<string>();

            if (resource.EnableGoogleBooks == true)
            {
                sources.Add("googlebooks");
            }

            if (resource.EnableLoc == true)
            {
                sources.Add("loc");
            }

            if (resource.EnableGutendex == true)
            {
                sources.Add("gutendex");
            }

            if (resource.EnableInternetArchive == true)
            {
                sources.Add("internetarchive");
            }

            if (resource.EnableNdl == true)
            {
                sources.Add("ndl");
            }

            if (resource.EnableEuropeana == true)
            {
                sources.Add("europeana");
            }

            if (resource.EnableApifyGoodreads == true)
            {
                sources.Add("apify-goodreads");
            }

            var dictionary = new Dictionary<string, object>();

            // Older UI/API clients do not send these fields. Leave an existing
            // provider selection untouched for those clients.
            if (sourceValues.Any(value => value.HasValue))
            {
                dictionary["AdditionalMetadataSources"] = string.Join(",", sources);
            }

            SaveSecret(dictionary, "HardcoverAuth", resource.HardcoverAuth, resource.ClearHardcoverAuth);
            SaveSecret(dictionary, "GoogleBooksApiKey", resource.GoogleBooksApiKey, resource.ClearGoogleBooksApiKey);
            SaveSecret(dictionary, "EuropeanaApiKey", resource.EuropeanaApiKey, resource.ClearEuropeanaApiKey);
            SaveSecret(dictionary, "ApifyToken", resource.ApifyToken, resource.ClearApifyToken);

            if (!resource.ApifyGoodreadsActorFromEnvironment)
            {
                SaveSecret(dictionary, "ApifyGoodreadsActor", resource.ApifyGoodreadsActor, resource.ClearApifyGoodreadsActor);
            }

            if (resource.ApifyGoodreadsInputTemplate != null && !resource.ApifyGoodreadsInputTemplateFromEnvironment)
            {
                dictionary["ApifyGoodreadsInputTemplate"] = resource.ApifyGoodreadsInputTemplate;
            }

            _configService.SaveConfigDictionary(dictionary);

            return Accepted(resource.Id > 0 ? resource.Id : 1);
        }

        private static void SaveSecret(
            IDictionary<string, object> dictionary,
            string configKey,
            string inputValue,
            bool clear)
        {
            if (!string.IsNullOrWhiteSpace(inputValue))
            {
                dictionary[configKey] = inputValue;
            }
            else if (clear)
            {
                dictionary[configKey] = string.Empty;
            }
        }
    }
}
