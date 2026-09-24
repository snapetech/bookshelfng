using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    /// <summary>
    /// Catalog source identifiers and compatibility rules for the runtime
    /// metadata catalog selection.
    /// </summary>
    public static class AdditionalMetadataSources
    {
        public const string OpenLibrary = "openlibrary";
        public const string Hardcover = "hardcover";
        public const string MetadataApi = "metadata-api";

        private static readonly HashSet<string> AdditionalSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "googlebooks",
            "loc",
            "gutendex",
            "internetarchive",
            "ndl",
            "europeana",
            "apify-goodreads",
            OpenLibrary
        };

        private static readonly HashSet<string> CatalogSources = new HashSet<string>(AdditionalSources, StringComparer.OrdinalIgnoreCase)
        {
            Hardcover,
            MetadataApi
        };

        // These sources can be fetched by AdditionalBookMetadataProxy and can
        // therefore be used for per-field ISBN-matched metadata preferences.
        public static bool IsSupportedSource(string source) =>
            !string.IsNullOrWhiteSpace(source) && AdditionalSources.Contains(source.Trim());

        public static bool IsSupportedCatalogSource(string source) =>
            !string.IsNullOrWhiteSpace(source) && CatalogSources.Contains(source.Trim());

        public static bool IsValidFieldPreference(string source) =>
            string.IsNullOrWhiteSpace(source) || IsSupportedSource(source);

        private static readonly HashSet<string> ManagedDefaultLists = new HashSet<string>(StringComparer.Ordinal)
        {
            "europeana,googlebooks",
            "europeana,googlebooks,gutendex",
            "europeana,googlebooks,loc",
            "europeana,googlebooks,gutendex,loc",
            "gutendex,loc",
            "loc,gutendex"
        };

        /// <summary>
        /// Resolves source selection, retaining the legacy additional-source
        /// setting and adding the previously implicit primary provider when
        /// no runtime catalog selection has been saved yet.
        /// </summary>
        public static HashSet<string> GetConfiguredRuntimeSources(
            string environmentSources,
            string runtimeSources,
            bool hasRuntimeSources,
            string legacyAdditionalSources,
            bool hasLegacyAdditionalSources,
            string googleBooksApiKey,
            string europeanaApiKey,
            string apifyActor,
            string apifyToken,
            string defaultPrimarySource)
        {
            string sourceList;

            if (IsEnvironmentOverride(environmentSources))
            {
                sourceList = environmentSources;
            }
            else if (hasRuntimeSources)
            {
                sourceList = runtimeSources ?? string.Empty;
            }
            else if (hasLegacyAdditionalSources)
            {
                sourceList = legacyAdditionalSources ?? string.Empty;
                sourceList = AddSource(sourceList, defaultPrimarySource);
            }
            else
            {
                sourceList = "loc,gutendex";
                sourceList = AddSource(sourceList, defaultPrimarySource);

                if (!googleBooksApiKey.IsNullOrWhiteSpace())
                {
                    sourceList = AddSource(sourceList, "googlebooks");
                }

                if (!europeanaApiKey.IsNullOrWhiteSpace())
                {
                    sourceList = AddSource(sourceList, "europeana");
                }
            }

            return Parse(sourceList);
        }

        public static HashSet<string> GetEnabledRuntimeSources(
            string environmentSources,
            string runtimeSources,
            bool hasRuntimeSources,
            string legacyAdditionalSources,
            bool hasLegacyAdditionalSources,
            string googleBooksApiKey,
            string europeanaApiKey,
            string apifyActor,
            string apifyToken,
            string defaultPrimarySource,
            bool hardcoverConfigured)
        {
            var enabled = GetConfiguredRuntimeSources(
                environmentSources,
                runtimeSources,
                hasRuntimeSources,
                legacyAdditionalSources,
                hasLegacyAdditionalSources,
                googleBooksApiKey,
                europeanaApiKey,
                apifyActor,
                apifyToken,
                defaultPrimarySource);

            if (googleBooksApiKey.IsNullOrWhiteSpace())
            {
                enabled.Remove("googlebooks");
            }

            if (europeanaApiKey.IsNullOrWhiteSpace())
            {
                enabled.Remove("europeana");
            }

            if (apifyActor.IsNullOrWhiteSpace() || apifyToken.IsNullOrWhiteSpace())
            {
                enabled.Remove("apify-goodreads");
            }

            if (!hardcoverConfigured)
            {
                enabled.Remove(Hardcover);
            }

            return enabled;
        }

        public static string GetEffectiveRuntimeSourceList(
            string environmentSources,
            string runtimeSources,
            bool hasRuntimeSources,
            string legacyAdditionalSources,
            bool hasLegacyAdditionalSources,
            string googleBooksApiKey,
            string europeanaApiKey,
            string apifyActor,
            string apifyToken,
            string defaultPrimarySource,
            bool hardcoverConfigured)
        {
            return string.Join(",", GetEnabledRuntimeSources(
                    environmentSources,
                    runtimeSources,
                    hasRuntimeSources,
                    legacyAdditionalSources,
                    hasLegacyAdditionalSources,
                    googleBooksApiKey,
                    europeanaApiKey,
                    apifyActor,
                    apifyToken,
                    defaultPrimarySource,
                    hardcoverConfigured)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        // Kept for callers and configuration created by earlier BookshelfNG
        // versions. These methods retain supplemental-provider semantics.
        public static HashSet<string> GetEnabledSources(
            string environmentSources,
            string savedSources,
            bool hasSavedSources,
            string googleBooksApiKey,
            string europeanaApiKey,
            string apifyActor,
            string apifyToken)
        {
            var enabled = GetConfiguredSources(
                environmentSources,
                savedSources,
                hasSavedSources,
                googleBooksApiKey,
                europeanaApiKey,
                apifyActor,
                apifyToken);

            if (googleBooksApiKey.IsNullOrWhiteSpace())
            {
                enabled.Remove("googlebooks");
            }

            if (europeanaApiKey.IsNullOrWhiteSpace())
            {
                enabled.Remove("europeana");
            }

            if (apifyActor.IsNullOrWhiteSpace() || apifyToken.IsNullOrWhiteSpace())
            {
                enabled.Remove("apify-goodreads");
            }

            return enabled;
        }

        public static HashSet<string> GetConfiguredSources(
            string environmentSources,
            string savedSources,
            bool hasSavedSources,
            string googleBooksApiKey,
            string europeanaApiKey,
            string apifyActor,
            string apifyToken)
        {
            var sourceList = ResolveSourceList(environmentSources, savedSources, hasSavedSources);

            if (sourceList == null)
            {
                sourceList = "loc,gutendex";

                if (!googleBooksApiKey.IsNullOrWhiteSpace())
                {
                    sourceList += ",googlebooks";
                }

                if (!europeanaApiKey.IsNullOrWhiteSpace())
                {
                    sourceList += ",europeana";
                }
            }

            return Parse(sourceList).Where(IsSupportedSource).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public static string GetEffectiveSourceList(
            string environmentSources,
            string savedSources,
            bool hasSavedSources,
            string googleBooksApiKey,
            string europeanaApiKey,
            string apifyActor,
            string apifyToken)
        {
            return string.Join(",", GetEnabledSources(
                    environmentSources,
                    savedSources,
                    hasSavedSources,
                    googleBooksApiKey,
                    europeanaApiKey,
                    apifyActor,
                    apifyToken)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        public static bool IsEnvironmentOverride(string environmentSources) =>
            environmentSources != null && !ManagedDefaultLists.Contains(NormalizeSourceList(environmentSources));

        private static HashSet<string> Parse(string sourceList) =>
            new HashSet<string>(
                (sourceList ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(IsSupportedCatalogSource),
                StringComparer.OrdinalIgnoreCase);

        private static string AddSource(string sourceList, string source)
        {
            if (!IsSupportedCatalogSource(source))
            {
                return sourceList;
            }

            return Parse(sourceList).Contains(source)
                ? sourceList
                : string.IsNullOrWhiteSpace(sourceList) ? source : sourceList + "," + source;
        }

        private static string ResolveSourceList(string environmentSources, string savedSources, bool hasSavedSources)
        {
            if (IsEnvironmentOverride(environmentSources))
            {
                return environmentSources;
            }

            if (hasSavedSources)
            {
                return savedSources ?? string.Empty;
            }

            return environmentSources;
        }

        private static string NormalizeSourceList(string sourceList) =>
            string.Join(",", (sourceList ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => !x.IsNullOrWhiteSpace())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal));
    }
}
