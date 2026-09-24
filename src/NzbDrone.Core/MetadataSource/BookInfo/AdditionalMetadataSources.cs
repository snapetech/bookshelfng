using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public static class AdditionalMetadataSources
    {
        public static HashSet<string> GetEnabledSources(
            string configuredSources,
            string googleBooksApiKey,
            string europeanaApiKey)
        {
            var sources = configuredSources;

            if (sources == null)
            {
                // Gutendex is a public, no-key source with a focused catalog of
                // Project Gutenberg titles. LOC remains the broad public fallback.
                sources = "loc,gutendex";

                if (!string.IsNullOrWhiteSpace(googleBooksApiKey))
                {
                    sources += ",googlebooks";
                }

                if (!string.IsNullOrWhiteSpace(europeanaApiKey))
                {
                    sources += ",europeana";
                }
            }

            return new HashSet<string>(
                sources.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
