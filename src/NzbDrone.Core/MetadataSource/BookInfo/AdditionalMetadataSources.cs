using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public static class AdditionalMetadataSources
    {
        private static readonly HashSet<string> SupportedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "googlebooks",
            "loc",
            "gutendex",
            "internetarchive",
            "ndl",
            "europeana",
            "apify-goodreads"
        };

        private static readonly HashSet<string> ManagedDefaultLists = new HashSet<string>(StringComparer.Ordinal)
        {
            "europeana,googlebooks",
            "europeana,googlebooks,gutendex",
            "europeana,googlebooks,loc",
            "europeana,googlebooks,gutendex,loc",
            "gutendex,loc"
        };

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
                // Gutendex is a public, no-key source with a focused catalog of
                // Project Gutenberg titles. LOC remains the broad public fallback.
                sourceList = "loc,gutendex";

                if (!string.IsNullOrWhiteSpace(googleBooksApiKey))
                {
                    sourceList += ",googlebooks";
                }

                if (!string.IsNullOrWhiteSpace(europeanaApiKey))
                {
                    sourceList += ",europeana";
                }
            }

            return new HashSet<string>(
                sourceList.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => !string.IsNullOrWhiteSpace(x) && SupportedSources.Contains(x)),
                StringComparer.OrdinalIgnoreCase);
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
