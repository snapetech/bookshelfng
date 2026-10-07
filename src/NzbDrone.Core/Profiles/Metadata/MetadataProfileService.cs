using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Profiles.Metadata
{
    public interface IMetadataProfileService
    {
        MetadataProfile Add(MetadataProfile profile);
        void Update(MetadataProfile profile);
        void Delete(int id);
        List<MetadataProfile> All();
        MetadataProfile Get(int id);
        bool Exists(int id);
        List<Book> FilterBooks(Author input, int profileId, int? ebookProfileId = null, int? audiobookProfileId = null);
    }

    public class MetadataProfileService : IMetadataProfileService, IHandle<ApplicationStartedEvent>
    {
        public const string NONE_PROFILE_NAME = "None";
        public const double NONE_PROFILE_MIN_POPULARITY = 1e10;

        private static readonly Regex PartOrSetRegex = new Regex(@"(?<from>\d+) of (?<to>\d+)|(?<from>\d+)\s?/\s?(?<to>\d+)|(?<from>\d+)\s?-\s?(?<to>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IMetadataProfileRepository _profileRepository;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IImportListFactory _importListFactory;
        private readonly IRootFolderService _rootFolderService;
        private readonly ITermMatcherService _termMatcherService;
        private readonly Logger _logger;

        public MetadataProfileService(IMetadataProfileRepository profileRepository,
                                      IAuthorService authorService,
                                      IBookService bookService,
                                      IEditionService editionService,
                                      IMediaFileService mediaFileService,
                                      IImportListFactory importListFactory,
                                      IRootFolderService rootFolderService,
                                      ITermMatcherService termMatcherService,
                                      Logger logger)
        {
            _profileRepository = profileRepository;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _importListFactory = importListFactory;
            _rootFolderService = rootFolderService;
            _termMatcherService = termMatcherService;
            _logger = logger;
        }

        public MetadataProfile Add(MetadataProfile profile)
        {
            return _profileRepository.Insert(profile);
        }

        public void Update(MetadataProfile profile)
        {
            if (profile.Name == NONE_PROFILE_NAME)
            {
                throw new InvalidOperationException("Not permitted to alter None metadata profile");
            }

            _profileRepository.Update(profile);
        }

        public void Delete(int id)
        {
            var profile = _profileRepository.Get(id);

            if (profile.Name == NONE_PROFILE_NAME ||
                _authorService.GetAllAuthors().Any(c => c.MetadataProfileId == id || c.EbookMetadataProfileId == id || c.AudiobookMetadataProfileId == id) ||
                _importListFactory.All().Any(c => c.MetadataProfileId == id) ||
                _rootFolderService.All().Any(c => c.DefaultMetadataProfileId == id))
            {
                throw new MetadataProfileInUseException(profile.Name);
            }

            _profileRepository.Delete(id);
        }

        public List<MetadataProfile> All()
        {
            return _profileRepository.All().ToList();
        }

        public MetadataProfile Get(int id)
        {
            return _profileRepository.Get(id);
        }

        public bool Exists(int id)
        {
            return _profileRepository.Exists(id);
        }

        public List<Book> FilterBooks(Author input, int profileId, int? ebookProfileId = null, int? audiobookProfileId = null)
        {
            var seriesLinks = input.Series?.Value?
                .Where(x => x.LinkItems != null && x.LinkItems.Value != null)
                .SelectMany(x => x.LinkItems.Value)
                .Where(x => x.Book != null && x.Book.Value != null)
                .GroupBy(x => x.Book.Value)
                .ToDictionary(x => x.Key, y => y.ToList()) ?? new Dictionary<Book, List<SeriesBookLink>>();

            var dbAuthor = _authorService.FindById(input.ForeignAuthorId);

            var localBooks = new List<Book>();
            if (dbAuthor != null)
            {
                localBooks = _bookService.GetBooksByAuthorMetadataId(dbAuthor.AuthorMetadataId);
                var editions = _editionService.GetEditionsByAuthor(dbAuthor.Id).GroupBy(x => x.BookId).ToDictionary(x => x.Key, y => y.ToList());

                foreach (var book in localBooks)
                {
                    if (editions.TryGetValue(book.Id, out var bookEditions))
                    {
                        book.Editions = bookEditions;
                    }
                    else
                    {
                        book.Editions = new List<Edition>();
                    }
                }
            }

            var localFiles = _mediaFileService.GetFilesByAuthor(dbAuthor?.Id ?? 0);

            return FilterBooks(input.Books.Value, localBooks, localFiles, seriesLinks, profileId, ebookProfileId, audiobookProfileId);
        }

        private List<Book> FilterBooks(IEnumerable<Book> remoteBooks, List<Book> localBooks, List<BookFile> localFiles, Dictionary<Book, List<SeriesBookLink>> seriesLinks, int metadataProfileId, int? ebookProfileId, int? audiobookProfileId)
        {
            var profile = Get(metadataProfileId);

            _logger.Trace($"Filtering:\n{remoteBooks.Select(x => x.ToString()).Join("\n")}");

            var hash = new HashSet<Book>(remoteBooks);
            var titles = new HashSet<string>(remoteBooks.Select(x => x.Title));

            var localBookIds = new HashSet<string>(localBooks.Where(x => x.AddOptions.AddType == BookAddType.Manual).Select(x => x.ForeignBookId));
            localBookIds.UnionWith(localFiles.Select(x => x.Edition.Value.Book.Value.ForeignBookId));

            var profiles = new Dictionary<int, MetadataProfile> { [profile.Id] = profile };
            foreach (var book in hash.ToList())
            {
                var localBook = localBookIds.Contains(book.ForeignBookId);
                var localEditions = localBooks.SingleOrDefault(x => x.ForeignBookId == book.ForeignBookId)?.Editions.Value ?? new List<Edition>();
                var localEditionIds = new HashSet<string>(localEditions.Where(x => x.ManualAdd).Select(x => x.ForeignEditionId));
                localEditionIds.UnionWith(localFiles.Select(x => x.Edition.Value.ForeignEditionId));
                var editions = book.Editions?.Value ?? new List<Edition>();
                var allowedEditions = new List<Edition>();

                foreach (var editionGroup in editions.GroupBy(x => GetMetadataProfileId(x, profile.Id, ebookProfileId, audiobookProfileId)))
                {
                    if (!profiles.TryGetValue(editionGroup.Key, out var editionProfile))
                    {
                        editionProfile = Get(editionGroup.Key);
                        profiles[editionGroup.Key] = editionProfile;
                    }

                    var profileEditions = editionGroup.ToList();
                    if (!localBook && !BookAllowedByProfile(book, profileEditions, editionProfile, seriesLinks.GetValueOrDefault(book), titles))
                    {
                        continue;
                    }

                    foreach (var edition in profileEditions)
                    {
                        if (!localEditionIds.Contains(edition.ForeignEditionId) && !EditionAllowedByProfile(edition, editionProfile))
                        {
                            _logger.Trace("Skipping edition {0} because it does not meet metadata profile {1}", edition, editionProfile.Name);
                            continue;
                        }

                        allowedEditions.Add(edition);
                    }
                }

                book.Editions = allowedEditions;
                if (allowedEditions.Count == 0 && !localBook)
                {
                    hash.Remove(book);
                }
            }

            return hash.ToList();
        }

        private static int GetMetadataProfileId(Edition edition, int profileId, int? ebookProfileId, int? audiobookProfileId)
        {
            if (edition.IsEbook && ebookProfileId.HasValue && ebookProfileId.Value > 0)
            {
                return ebookProfileId.Value;
            }

            if (MediaFileExtensions.IsAudiobookEdition(edition) && audiobookProfileId.HasValue && audiobookProfileId.Value > 0)
            {
                return audiobookProfileId.Value;
            }

            return profileId;
        }

        private bool BookAllowedByProfile(Book book, List<Edition> editions, MetadataProfile profile, List<SeriesBookLink> seriesLinks, HashSet<string> titles)
        {
            var allowed = BookAllowedByRating(book, profile) &&
                (!profile.SkipMissingDate || book.ReleaseDate.HasValue) &&
                (!profile.SkipPartsAndSets || !IsPartOrSet(book, seriesLinks, titles)) &&
                (!profile.SkipSeriesSecondary || seriesLinks == null || seriesLinks.Any(x => x.IsPrimary)) &&
                !profile.Ignored.Any(i => MatchesTerms(book.Title, i));

            if (allowed && !(editions.Any(x => x.PageCount > profile.MinPages) || editions.All(x => x.PageCount == 0)))
            {
                _logger.Trace("Skipping book {0} because it does not meet the minimum page count in metadata profile {1}", book, profile.Name);
                return false;
            }

            if (!allowed)
            {
                _logger.Trace("Skipping book {0} because it does not meet metadata profile {1}", book, profile.Name);
            }

            return allowed;
        }

        private bool EditionAllowedByProfile(Edition edition, MetadataProfile profile)
        {
            var allowedLanguages = profile.AllowedLanguages.IsNotNullOrWhiteSpace()
                ? new HashSet<string>(profile.AllowedLanguages.Trim(',').Split(',').Select(x => x.CanonicalizeLanguage()))
                : new HashSet<string>();

            return (!allowedLanguages.Any() || allowedLanguages.Contains(edition.Language?.CanonicalizeLanguage())) &&
                (!profile.SkipMissingIsbn || edition.Isbn13.IsNotNullOrWhiteSpace() || edition.Asin.IsNotNullOrWhiteSpace()) &&
                !profile.Ignored.Any(i => MatchesTerms(edition.Title, i));
        }

        private bool BookAllowedByRating(Book b, MetadataProfile p)
        {
            // hack for the 'none' metadata profile
            if (p.MinPopularity == NONE_PROFILE_MIN_POPULARITY)
            {
                return false;
            }

            return (b.Ratings.Popularity >= p.MinPopularity) || b.ReleaseDate > DateTime.UtcNow;
        }

        private bool IsPartOrSet(Book book, List<SeriesBookLink> seriesLinks, HashSet<string> titles)
        {
            if (seriesLinks != null &&
                seriesLinks.Any(x => x.Position.IsNotNullOrWhiteSpace()) &&
                !seriesLinks.Any(s => double.TryParse(s.Position, out _)))
            {
                // No non-empty series entries parse to a number, so all like 1-3 etc.
                return true;
            }

            // Skip things of form Title1 / Title2 when Title1 and Title2 are already in the list
            var bookTitles = new[] { book.Title }.Concat(book.Editions.Value.Select(x => x.Title)).ToList();
            foreach (var title in bookTitles)
            {
                var split = title.Split('/').Select(x => x.Trim()).ToList();
                if (split.Count > 1 && split.All(x => titles.Contains(x)))
                {
                    return true;
                }
            }

            var match = PartOrSetRegex.Match(book.Title);

            if (match.Groups["from"].Success)
            {
                var from = int.Parse(match.Groups["from"].Value);
                return from <= 1800 || from > DateTime.UtcNow.Year;
            }

            return false;
        }

        private bool MatchesTerms(string value, string terms)
        {
            if (terms.IsNullOrWhiteSpace() || value.IsNullOrWhiteSpace())
            {
                return false;
            }

            var split = terms.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var foundTerms = ContainsAny(split, value);

            return foundTerms.Any();
        }

        private List<string> ContainsAny(List<string> terms, string title)
        {
            return terms.Where(t => _termMatcherService.IsMatch(t, title)).ToList();
        }

        public void Handle(ApplicationStartedEvent message)
        {
            var profiles = All();

            // Name is a unique property
            var emptyProfile = profiles.FirstOrDefault(x => x.Name == NONE_PROFILE_NAME);

            // make sure empty profile exists and is actually empty
            // TODO: reinstate
            if (emptyProfile != null &&
                emptyProfile.MinPopularity == NONE_PROFILE_MIN_POPULARITY)
            {
                return;
            }

            if (!profiles.Any())
            {
                _logger.Info("Setting up standard metadata profile");

                var minPopularity = 350;
                var hc = string.Equals(Environment.GetEnvironmentVariable("HARDCOVER"), "true", StringComparison.OrdinalIgnoreCase);
                if (hc)
                {
                    minPopularity = 50;
                }

                Add(new MetadataProfile
                {
                    Name = "Standard",
                    MinPopularity = minPopularity,
                    SkipMissingDate = true,
                    SkipPartsAndSets = true,
                    AllowedLanguages = "eng, null"
                });
            }

            if (emptyProfile != null)
            {
                // emptyProfile is not the correct empty profile - move it out of the way
                _logger.Info($"Renaming non-empty metadata profile {emptyProfile.Name}");

                var names = profiles.Select(x => x.Name).ToList();

                var i = 1;
                emptyProfile.Name = $"{NONE_PROFILE_NAME}.{i}";

                while (names.Contains(emptyProfile.Name))
                {
                    i++;
                }

                _profileRepository.Update(emptyProfile);
            }

            _logger.Info("Setting up empty metadata profile");

            Add(new MetadataProfile
            {
                Name = NONE_PROFILE_NAME,
                MinPopularity = NONE_PROFILE_MIN_POPULARITY
            });
        }
    }
}
