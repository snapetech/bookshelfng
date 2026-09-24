using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.BookInfo;
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
            var legacySourceValues = new[]
            {
                resource.EnableGoogleBooks,
                resource.EnableLoc,
                resource.EnableGutendex,
                resource.EnableInternetArchive,
                resource.EnableNdl,
                resource.EnableEuropeana,
                resource.EnableApifyGoodreads
            };
            var runtimeSourceValues = new[]
            {
                resource.EnableOpenLibrary,
                resource.EnableHardcover,
                resource.EnableMetadataApi
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

            if (resource.EnableOpenLibrary == true)
            {
                sources.Add(AdditionalMetadataSources.OpenLibrary);
            }

            if (resource.EnableHardcover == true)
            {
                sources.Add(AdditionalMetadataSources.Hardcover);
            }

            if (resource.EnableMetadataApi == true)
            {
                sources.Add(AdditionalMetadataSources.MetadataApi);
            }

            var dictionary = new Dictionary<string, object>();

            var fieldSourcePreferences = new Dictionary<string, string>
            {
                ["MetadataTitleSourcePreference"] = resource.MetadataTitleSourcePreference,
                ["MetadataDescriptionSourcePreference"] = resource.MetadataDescriptionSourcePreference,
                ["MetadataPublisherSourcePreference"] = resource.MetadataPublisherSourcePreference,
                ["MetadataLanguageSourcePreference"] = resource.MetadataLanguageSourcePreference,
                ["MetadataReleaseDateSourcePreference"] = resource.MetadataReleaseDateSourcePreference,
                ["MetadataPageCountSourcePreference"] = resource.MetadataPageCountSourcePreference,
                ["MetadataCoverSourcePreference"] = resource.MetadataCoverSourcePreference,
                ["MetadataGenresSourcePreference"] = resource.MetadataGenresSourcePreference
            };

            if (fieldSourcePreferences.Values.Any(value => value != null))
            {
                foreach (var preference in fieldSourcePreferences)
                {
                    if (preference.Value != null && !AdditionalMetadataSources.IsValidFieldPreference(preference.Value))
                    {
                        return BadRequest("Metadata field source preference is not supported.");
                    }

                    if (preference.Value != null)
                    {
                        dictionary[preference.Key] = preference.Value;
                    }
                }
            }

            // Older UI/API clients do not send these fields. Leave an existing
            // provider selection untouched for those clients.
            if (runtimeSourceValues.Any(value => value.HasValue))
            {
                dictionary["MetadataCatalogSources"] = string.Join(",", sources);
            }
            else if (!_configService.IsDefined("MetadataCatalogSources") && legacySourceValues.Any(value => value.HasValue))
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

            if (resource.OpenLibraryContactEmail != null && !resource.OpenLibraryContactEmailFromEnvironment)
            {
                dictionary["OpenLibraryContactEmail"] = resource.OpenLibraryContactEmail.Trim();
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
