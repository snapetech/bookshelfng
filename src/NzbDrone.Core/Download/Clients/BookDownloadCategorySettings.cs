using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Clients
{
    public interface IBookDownloadCategorySettings
    {
        string MusicCategory { get; }
        string EbookCategory { get; }
        string AudiobookCategory { get; }
    }

    public static class BookDownloadCategorySettings
    {
        public static string GetCategory(IBookDownloadCategorySettings settings, ReleaseInfo release)
        {
            if (settings == null)
            {
                return null;
            }

            if (IsAudiobook(release))
            {
                return FirstConfigured(settings.AudiobookCategory, settings.MusicCategory);
            }

            if (IsEbook(release))
            {
                return FirstConfigured(settings.EbookCategory, settings.MusicCategory);
            }

            return settings.MusicCategory;
        }

        public static IReadOnlyList<string> GetConfiguredCategories(IBookDownloadCategorySettings settings)
        {
            if (settings == null)
            {
                return Array.Empty<string>();
            }

            return new[] { settings.MusicCategory, settings.EbookCategory, settings.AudiobookCategory }
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Select(category => category.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool MatchesConfiguredCategory(IBookDownloadCategorySettings settings, string category)
        {
            var configured = GetConfiguredCategories(settings);
            return configured.Count == 0
                ? string.IsNullOrWhiteSpace(category) || category == "*"
                : configured.Contains(category, StringComparer.OrdinalIgnoreCase);
        }

        public static string FindCategoryInPath(IBookDownloadCategorySettings settings, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return GetConfiguredCategories(settings)
                .FirstOrDefault(category => segments.Any(segment => string.Equals(segment, category, StringComparison.OrdinalIgnoreCase)));
        }

        public static string GetCategoryFieldName(IBookDownloadCategorySettings settings, string category)
        {
            if (settings == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(category) &&
                string.Equals(settings.EbookCategory, category, StringComparison.OrdinalIgnoreCase))
            {
                return "EbookCategory";
            }

            if (!string.IsNullOrWhiteSpace(category) &&
                string.Equals(settings.AudiobookCategory, category, StringComparison.OrdinalIgnoreCase))
            {
                return "AudiobookCategory";
            }

            return "MusicCategory";
        }

        private static bool IsAudiobook(ReleaseInfo release)
        {
            return release?.Categories?.Contains(NewznabStandardCategory.AudioAudiobook.Id) == true ||
                   IsAudioExtension(release?.Container) ||
                   IsAudioExtension(Path.GetExtension(release?.Title));
        }

        private static bool IsEbook(ReleaseInfo release)
        {
            return release?.DownloadProtocol == DownloadProtocol.Direct ||
                   release?.Categories?.Contains(NewznabStandardCategory.BooksEBook.Id) == true ||
                   IsTextExtension(release?.Container) ||
                   IsTextExtension(Path.GetExtension(release?.Title));
        }

        private static bool IsAudioExtension(string extension)
        {
            return MediaFileExtensions.AudioExtensions.Contains(NormalizeExtension(extension));
        }

        private static bool IsTextExtension(string extension)
        {
            return MediaFileExtensions.TextExtensions.Contains(NormalizeExtension(extension));
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return null;
            }

            var candidate = extension.Contains('.') ? Path.GetExtension(extension) : $".{extension}";
            return candidate;
        }

        private static string FirstConfigured(string preferred, string fallback)
        {
            return !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;
        }
    }
}
