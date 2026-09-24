using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Books
{
    public static class SeriesBookLinkExtensions
    {
        public static SeriesBookLink GetPreferredSeriesLink(this IEnumerable<SeriesBookLink> seriesLinks, string preferredSeries = null)
        {
            var preferredTerms = (preferredSeries ?? string.Empty)
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToList();

            return seriesLinks?
                .Where(link => link != null && !string.IsNullOrWhiteSpace(link.Series?.Value?.Title))
                .OrderBy(link => GetPreferredTermIndex(link.Series.Value.Title, preferredTerms))
                .ThenByDescending(link => link.IsPrimary)
                .ThenBy(link => link.SeriesPosition > 0 ? link.SeriesPosition : int.MaxValue)
                .ThenBy(link => link.Series.Value.Title, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static int GetPreferredTermIndex(string seriesTitle, List<string> preferredTerms)
        {
            if (!preferredTerms.Any())
            {
                return int.MaxValue;
            }

            var index = preferredTerms.FindIndex(term => seriesTitle.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

            return index >= 0 ? index : int.MaxValue;
        }
    }
}
