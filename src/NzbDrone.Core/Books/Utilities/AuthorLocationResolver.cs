using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Books
{
    public static class AuthorLocationResolver
    {
        public static string GetPath(Author author, string extension)
        {
            if (author == null)
            {
                return null;
            }

            if (MediaFileExtensions.AudioExtensions.Contains(extension) && author.AudiobookPath.IsNotNullOrWhiteSpace())
            {
                return author.AudiobookPath;
            }

            if (MediaFileExtensions.TextExtensions.Contains(extension) && author.EbookPath.IsNotNullOrWhiteSpace())
            {
                return author.EbookPath;
            }

            return author.Path;
        }

        public static List<string> GetPaths(Author author)
        {
            return new[] { author.Path, author.EbookPath, author.AudiobookPath }
                .Where(path => path.IsNotNullOrWhiteSpace())
                .Distinct(PathEqualityComparer.Instance)
                .ToList();
        }

        public static string GetPathForFile(Author author, string filePath)
        {
            return GetPath(author, Path.GetExtension(filePath));
        }
    }
}
