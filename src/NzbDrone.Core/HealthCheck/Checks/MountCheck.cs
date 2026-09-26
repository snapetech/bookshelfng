using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    public class MountCheck : HealthCheckBase
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IAuthorService _authorService;

        public MountCheck(IDiskProvider diskProvider, IAuthorService authorService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _diskProvider = diskProvider;
            _authorService = authorService;
        }

        public override HealthCheck Check()
        {
            // Not best for optimization but due to possible symlinks and junctions, get mounts from each configured author location.
            var authorPaths = _authorService.AllAuthorLocationPaths();
            var paths = authorPaths == null ?
                _authorService.AllAuthorPaths().Select(p => p.Value) :
                authorPaths.Select(p => p.Value);
            var mounts = paths
                                      .Select(path => _diskProvider.GetMount(path))
                                      .Where(m => m != null && m.MountOptions != null && m.MountOptions.IsReadOnly)
                                      .DistinctBy(m => m.RootDirectory)
                                      .ToList();

            if (mounts.Any())
            {
                return new HealthCheck(GetType(), HealthCheckResult.Error, _localizationService.GetLocalizedString("MountCheckMessage") + string.Join(", ", mounts.Select(m => m.Name)), "#author-mount-ro");
            }

            return new HealthCheck(GetType());
        }
    }
}
