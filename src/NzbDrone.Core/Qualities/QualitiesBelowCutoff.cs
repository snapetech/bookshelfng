using System.Collections.Generic;

namespace NzbDrone.Core.Qualities
{
    public class QualitiesBelowCutoff
    {
        public int ProfileId { get; set; }
        public IEnumerable<int> QualityIds { get; set; }
        public bool IsAudio { get; set; }
        public bool IsFormatOverride { get; set; }

        public QualitiesBelowCutoff(int profileId, IEnumerable<int> qualityIds, bool isAudio = false, bool isFormatOverride = false)
        {
            ProfileId = profileId;
            QualityIds = qualityIds;
            IsAudio = isAudio;
            IsFormatOverride = isFormatOverride;
        }
    }
}
