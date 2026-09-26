using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(045)]
    public class add_author_format_paths : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Authors").AddColumn("EbookPath").AsString().Nullable();
            Alter.Table("Authors").AddColumn("AudiobookPath").AsString().Nullable();
        }
    }
}
