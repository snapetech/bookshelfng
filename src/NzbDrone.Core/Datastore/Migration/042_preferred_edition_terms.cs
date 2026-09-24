using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(042)]
    public class preferred_edition_terms : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("MetadataProfiles").AddColumn("PreferredEditionTerms").AsString().Nullable();
        }
    }
}
