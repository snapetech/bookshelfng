using System;
using System.Collections.Generic;
using Equ;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    public class Author : Entity<Author>
    {
        public Author()
        {
            Tags = new HashSet<int>();
            Metadata = new AuthorMetadata();
        }

        // These correspond to columns in the Authors table
        public int AuthorMetadataId { get; set; }
        public string CleanName { get; set; }
        public bool Monitored { get; set; }
        public NewItemMonitorTypes MonitorNewItems { get; set; }
        public DateTime? LastInfoSync { get; set; }
        public string Path { get; set; }
        public string EbookPath { get; set; }
        public string AudiobookPath { get; set; }
        public string RootFolderPath { get; set; }
        public DateTime Added { get; set; }
        public int QualityProfileId { get; set; }
        public int MetadataProfileId { get; set; }
        public int? EbookQualityProfileId { get; set; }
        public int? AudiobookQualityProfileId { get; set; }
        public int? EbookMetadataProfileId { get; set; }
        public int? AudiobookMetadataProfileId { get; set; }
        public HashSet<int> Tags { get; set; }
        [MemberwiseEqualityIgnore]
        public AddAuthorOptions AddOptions { get; set; }

        // Dynamically loaded from DB
        [MemberwiseEqualityIgnore]
        public LazyLoaded<AuthorMetadata> Metadata { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<QualityProfile> QualityProfile { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<MetadataProfile> MetadataProfile { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<QualityProfile> EbookQualityProfile { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<QualityProfile> AudiobookQualityProfile { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<MetadataProfile> EbookMetadataProfile { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<MetadataProfile> AudiobookMetadataProfile { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<List<Book>> Books { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<List<Series>> Series { get; set; }

        public QualityProfile GetQualityProfileFor(Quality quality)
        {
            if (quality != null && MediaFileExtensions.IsAudioQuality(quality) && AudiobookQualityProfileId.HasValue && AudiobookQualityProfile != null)
            {
                return AudiobookQualityProfile.Value;
            }

            if (quality != null && !MediaFileExtensions.IsAudioQuality(quality) && EbookQualityProfileId.HasValue && EbookQualityProfile != null)
            {
                return EbookQualityProfile.Value;
            }

            return QualityProfile?.Value;
        }

        public MetadataProfile GetMetadataProfileFor(Edition edition)
        {
            if (edition?.IsEbook == true && EbookMetadataProfileId.HasValue && EbookMetadataProfile != null)
            {
                return EbookMetadataProfile.Value;
            }

            if (edition != null && MediaFileExtensions.IsAudiobookEdition(edition) && AudiobookMetadataProfileId.HasValue && AudiobookMetadataProfile != null)
            {
                return AudiobookMetadataProfile.Value;
            }

            return MetadataProfile.Value;
        }

        //compatibility properties
        [MemberwiseEqualityIgnore]
        public string Name
        {
            get { return Metadata.Value.Name; } set { Metadata.Value.Name = value; }
        }

        [MemberwiseEqualityIgnore]
        public string ForeignAuthorId
        {
            get { return Metadata.Value.ForeignAuthorId; } set { Metadata.Value.ForeignAuthorId = value; }
        }

        public override string ToString()
        {
            return string.Format("[{0}][{1}]", Metadata.Value.ForeignAuthorId.NullSafe(), Metadata.Value.Name.NullSafe());
        }

        public override void UseMetadataFrom(Author other)
        {
            CleanName = other.CleanName;
        }

        public override void UseDbFieldsFrom(Author other)
        {
            Id = other.Id;
            AuthorMetadataId = other.AuthorMetadataId;
            Monitored = other.Monitored;
            MonitorNewItems = other.MonitorNewItems;
            LastInfoSync = other.LastInfoSync;
            Path = other.Path;
            EbookPath = other.EbookPath;
            AudiobookPath = other.AudiobookPath;
            RootFolderPath = other.RootFolderPath;
            Added = other.Added;
            QualityProfileId = other.QualityProfileId;
            QualityProfile = other.QualityProfile;
            MetadataProfileId = other.MetadataProfileId;
            MetadataProfile = other.MetadataProfile;
            EbookQualityProfileId = other.EbookQualityProfileId;
            AudiobookQualityProfileId = other.AudiobookQualityProfileId;
            EbookMetadataProfileId = other.EbookMetadataProfileId;
            AudiobookMetadataProfileId = other.AudiobookMetadataProfileId;
            EbookQualityProfile = other.EbookQualityProfile;
            AudiobookQualityProfile = other.AudiobookQualityProfile;
            EbookMetadataProfile = other.EbookMetadataProfile;
            AudiobookMetadataProfile = other.AudiobookMetadataProfile;
            Tags = other.Tags;
            AddOptions = other.AddOptions;
        }

        public override void ApplyChanges(Author other)
        {
            Path = other.Path;
            EbookPath = other.EbookPath;
            AudiobookPath = other.AudiobookPath;
            QualityProfileId = other.QualityProfileId;
            QualityProfile = other.QualityProfile;
            MetadataProfileId = other.MetadataProfileId;
            MetadataProfile = other.MetadataProfile;
            EbookQualityProfileId = other.EbookQualityProfileId;
            AudiobookQualityProfileId = other.AudiobookQualityProfileId;
            EbookMetadataProfileId = other.EbookMetadataProfileId;
            AudiobookMetadataProfileId = other.AudiobookMetadataProfileId;
            EbookQualityProfile = other.EbookQualityProfile;
            AudiobookQualityProfile = other.AudiobookQualityProfile;
            EbookMetadataProfile = other.EbookMetadataProfile;
            AudiobookMetadataProfile = other.AudiobookMetadataProfile;

            Books = other.Books;
            Tags = other.Tags;
            AddOptions = other.AddOptions;
            RootFolderPath = other.RootFolderPath;
            Monitored = other.Monitored;
            MonitorNewItems = other.MonitorNewItems;
        }
    }
}
