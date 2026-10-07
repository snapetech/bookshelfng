using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    public interface IBookCutoffService
    {
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec);
    }

    public class BookCutoffService : IBookCutoffService
    {
        private readonly IBookRepository _bookRepository;
        private readonly IQualityProfileService _qualityProfileService;

        public BookCutoffService(IBookRepository bookRepository, IQualityProfileService qualityProfileService)
        {
            _bookRepository = bookRepository;
            _qualityProfileService = qualityProfileService;
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec)
        {
            var qualitiesBelowCutoff = new List<QualitiesBelowCutoff>();
            var profiles = _qualityProfileService.All();

            //Get all items less than the cutoff
            foreach (var profile in profiles)
            {
                var cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirstAllowedQuality().Id;
                var cutoffIndex = profile.GetIndex(cutoff);
                var belowCutoff = profile.Items.Take(cutoffIndex.Index).ToList();

                if (belowCutoff.Any())
                {
                    var qualityIds = belowCutoff.SelectMany(i => i.GetQualities()).ToList();
                    var ebookIds = qualityIds.Where(q => !MediaFileExtensions.IsAudioQuality(q)).Select(q => q.Id).ToList();
                    var audiobookIds = qualityIds.Where(MediaFileExtensions.IsAudioQuality).Select(q => q.Id).ToList();
                    if (ebookIds.Any())
                    {
                        qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, ebookIds));
                        qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, ebookIds, isFormatOverride: true));
                    }

                    if (audiobookIds.Any())
                    {
                        qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, audiobookIds, isAudio: true));
                        qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, audiobookIds, isAudio: true, isFormatOverride: true));
                    }
                }
            }

            if (qualitiesBelowCutoff.Empty())
            {
                pagingSpec.Records = new List<Book>();

                return pagingSpec;
            }

            return _bookRepository.BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff);
        }
    }
}
