using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public static class AdditionalMetadataSources
    {
        public static HashSet<string> GetEnabledSources(string configuredSources, string googleBooksApiKey)
        {
            var sources = configuredSources;

            if (sources == null)
            {
                sources = "loc";

                if (!string.IsNullOrWhiteSpace(googleBooksApiKey))
                {
                    sources += ",googlebooks";
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
