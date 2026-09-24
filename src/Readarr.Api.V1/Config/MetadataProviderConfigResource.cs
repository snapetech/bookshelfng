using System;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.BookInfo;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Config
{
    public class MetadataProviderConfigResource : RestResource
    {
        public WriteAudioTagsType WriteAudioTags { get; set; }
        public bool ScrubAudioTags { get; set; }
        public WriteBookTagsType WriteBookTags { get; set; }
        public bool UpdateCovers { get; set; }
        public bool EmbedMetadata { get; set; }
        public string HardcoverAuth { get; set; }
        public bool HasHardcoverAuth { get; set; }
        public bool HardcoverAuthFromEnvironment { get; set; }
        public bool ClearHardcoverAuth { get; set; }

        public bool? EnableGoogleBooks { get; set; }
        public bool? EnableLoc { get; set; }
        public bool? EnableGutendex { get; set; }
        public bool? EnableInternetArchive { get; set; }
        public bool? EnableNdl { get; set; }
        public bool? EnableEuropeana { get; set; }
        public bool? EnableApifyGoodreads { get; set; }
        public bool SourcesFromEnvironment { get; set; }

        public string MetadataTitleSourcePreference { get; set; }
        public string MetadataDescriptionSourcePreference { get; set; }
        public string MetadataPublisherSourcePreference { get; set; }
        public string MetadataLanguageSourcePreference { get; set; }
        public string MetadataReleaseDateSourcePreference { get; set; }
        public string MetadataPageCountSourcePreference { get; set; }
        public string MetadataCoverSourcePreference { get; set; }
        public string MetadataGenresSourcePreference { get; set; }

        // Credential inputs are write-only: GET returns an empty input plus a
        // presence flag so saved API keys and tokens never leave the server.
        public string GoogleBooksApiKey { get; set; }
        public bool HasGoogleBooksApiKey { get; set; }
        public bool GoogleBooksApiKeyFromEnvironment { get; set; }
        public bool ClearGoogleBooksApiKey { get; set; }
        public string EuropeanaApiKey { get; set; }
        public bool HasEuropeanaApiKey { get; set; }
        public bool EuropeanaApiKeyFromEnvironment { get; set; }
        public bool ClearEuropeanaApiKey { get; set; }
        public string ApifyGoodreadsActor { get; set; }
        public bool ApifyGoodreadsActorFromEnvironment { get; set; }
        public bool ClearApifyGoodreadsActor { get; set; }
        public string ApifyToken { get; set; }
        public bool HasApifyToken { get; set; }
        public bool ApifyTokenFromEnvironment { get; set; }
        public bool ClearApifyToken { get; set; }
        public string ApifyGoodreadsInputTemplate { get; set; }
        public bool ApifyGoodreadsInputTemplateFromEnvironment { get; set; }
    }

    public static class MetadataProviderConfigResourceMapper
    {
        public static MetadataProviderConfigResource ToResource(IConfigService model)
        {
            var environmentSources = Environment.GetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES");
            var hardcoverAuthFromEnvironment = Environment.GetEnvironmentVariable("HARDCOVER_AUTH");
            var hardcoverApiKeyFromEnvironment = Environment.GetEnvironmentVariable("HARDCOVER_API_KEY");
            var googleBooksApiKeyFromEnvironment = Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY");
            var europeanaApiKeyFromEnvironment = Environment.GetEnvironmentVariable("EUROPEANA_API_KEY");
            var apifyActorFromEnvironment = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_ACTOR");
            var apifyTokenFromEnvironment = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_TOKEN");
            var apifyInputTemplateFromEnvironment = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_INPUT_TEMPLATE");
            var googleBooksApiKey = EffectiveValue(googleBooksApiKeyFromEnvironment, model.GoogleBooksApiKey);
            var europeanaApiKey = EffectiveValue(europeanaApiKeyFromEnvironment, model.EuropeanaApiKey);
            var hardcoverAuth = EffectiveValue(
                hardcoverAuthFromEnvironment,
                EffectiveValue(hardcoverApiKeyFromEnvironment, model.HardcoverAuth));
            var apifyActor = EffectiveValue(apifyActorFromEnvironment, model.ApifyGoodreadsActor);
            var apifyToken = EffectiveValue(apifyTokenFromEnvironment, model.ApifyToken);
            var configuredSources = AdditionalMetadataSources.GetConfiguredSources(
                environmentSources,
                model.AdditionalMetadataSources,
                model.IsDefined("AdditionalMetadataSources"),
                googleBooksApiKey,
                europeanaApiKey,
                apifyActor,
                apifyToken);

            return new MetadataProviderConfigResource
            {
                WriteAudioTags = model.WriteAudioTags,
                ScrubAudioTags = model.ScrubAudioTags,
                WriteBookTags = model.WriteBookTags,
                UpdateCovers = model.UpdateCovers,
                EmbedMetadata = model.EmbedMetadata,
                HardcoverAuth = string.Empty,
                HasHardcoverAuth = !string.IsNullOrWhiteSpace(hardcoverAuth),
                HardcoverAuthFromEnvironment = !string.IsNullOrWhiteSpace(hardcoverAuthFromEnvironment) || !string.IsNullOrWhiteSpace(hardcoverApiKeyFromEnvironment),
                ClearHardcoverAuth = false,
                EnableGoogleBooks = configuredSources.Contains("googlebooks"),
                EnableLoc = configuredSources.Contains("loc"),
                EnableGutendex = configuredSources.Contains("gutendex"),
                EnableInternetArchive = configuredSources.Contains("internetarchive"),
                EnableNdl = configuredSources.Contains("ndl"),
                EnableEuropeana = configuredSources.Contains("europeana"),
                EnableApifyGoodreads = configuredSources.Contains("apify-goodreads"),
                SourcesFromEnvironment = AdditionalMetadataSources.IsEnvironmentOverride(environmentSources),
                MetadataTitleSourcePreference = model.MetadataTitleSourcePreference,
                MetadataDescriptionSourcePreference = model.MetadataDescriptionSourcePreference,
                MetadataPublisherSourcePreference = model.MetadataPublisherSourcePreference,
                MetadataLanguageSourcePreference = model.MetadataLanguageSourcePreference,
                MetadataReleaseDateSourcePreference = model.MetadataReleaseDateSourcePreference,
                MetadataPageCountSourcePreference = model.MetadataPageCountSourcePreference,
                MetadataCoverSourcePreference = model.MetadataCoverSourcePreference,
                MetadataGenresSourcePreference = model.MetadataGenresSourcePreference,
                GoogleBooksApiKey = string.Empty,
                HasGoogleBooksApiKey = !string.IsNullOrWhiteSpace(googleBooksApiKey),
                GoogleBooksApiKeyFromEnvironment = !string.IsNullOrWhiteSpace(googleBooksApiKeyFromEnvironment),
                ClearGoogleBooksApiKey = false,
                EuropeanaApiKey = string.Empty,
                HasEuropeanaApiKey = !string.IsNullOrWhiteSpace(europeanaApiKey),
                EuropeanaApiKeyFromEnvironment = !string.IsNullOrWhiteSpace(europeanaApiKeyFromEnvironment),
                ClearEuropeanaApiKey = false,
                ApifyGoodreadsActor = apifyActor ?? string.Empty,
                ApifyGoodreadsActorFromEnvironment = !string.IsNullOrWhiteSpace(apifyActorFromEnvironment),
                ClearApifyGoodreadsActor = false,
                ApifyToken = string.Empty,
                HasApifyToken = !string.IsNullOrWhiteSpace(apifyToken),
                ApifyTokenFromEnvironment = !string.IsNullOrWhiteSpace(apifyTokenFromEnvironment),
                ClearApifyToken = false,
                ApifyGoodreadsInputTemplate = EffectiveValue(apifyInputTemplateFromEnvironment, model.ApifyGoodreadsInputTemplate),
                ApifyGoodreadsInputTemplateFromEnvironment = !string.IsNullOrWhiteSpace(apifyInputTemplateFromEnvironment)
            };
        }

        private static string EffectiveValue(string environmentValue, string savedValue)
        {
            return string.IsNullOrWhiteSpace(environmentValue) ? savedValue ?? string.Empty : environmentValue;
        }
    }
}
