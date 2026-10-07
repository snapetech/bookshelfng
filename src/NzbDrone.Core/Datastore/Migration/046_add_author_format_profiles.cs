using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(046)]
    public class add_author_format_profiles : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Authors")
                .AddColumn("EbookQualityProfileId").AsInt32().Nullable()
                .AddColumn("AudiobookQualityProfileId").AsInt32().Nullable()
                .AddColumn("EbookMetadataProfileId").AsInt32().Nullable()
                .AddColumn("AudiobookMetadataProfileId").AsInt32().Nullable();
        }
    }
}
