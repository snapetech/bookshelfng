using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IAuthorRepository : IBasicRepository<Author>
    {
        bool AuthorPathExists(string path);
        Author FindByName(string cleanName);
        Author FindById(string foreignAuthorId);
        Dictionary<int, string> AllAuthorPaths();
        List<KeyValuePair<int, string>> AllAuthorLocationPaths();
        Dictionary<int, List<int>> AllAuthorTags();
        Author GetAuthorByMetadataId(int authorMetadataId);
        List<Author> GetAuthorsByMetadataId(IEnumerable<int> authorMetadataId);
    }

    public class AuthorRepository : BasicRepository<Author>, IAuthorRepository
    {
        public AuthorRepository(IMainDatabase database,
                                IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        protected override SqlBuilder Builder() => new SqlBuilder(_database.DatabaseType)
            .Join<Author, AuthorMetadata>((a, m) => a.AuthorMetadataId == m.Id);

        protected override List<Author> Query(SqlBuilder builder) => Query(_database, builder).ToList();

        public static IEnumerable<Author> Query(IDatabase database, SqlBuilder builder)
        {
            return database.QueryJoined<Author, AuthorMetadata>(builder, (author, metadata) =>
                    {
                        author.Metadata = metadata;
                        return author;
                    });
        }

        public bool AuthorPathExists(string path)
        {
            return Query(c => c.Path == path || c.EbookPath == path || c.AudiobookPath == path).Any();
        }

        public Author FindById(string foreignAuthorId)
        {
            return Query(Builder().Where<AuthorMetadata>(m => m.ForeignAuthorId == foreignAuthorId)).SingleOrDefault();
        }

        public Author FindByName(string cleanName)
        {
            cleanName = cleanName.ToLowerInvariant();

            return Query(s => s.CleanName == cleanName).ExclusiveOrDefault();
        }

        public Dictionary<int, string> AllAuthorPaths()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS \"Key\", \"Path\" AS \"Value\" FROM \"Authors\"";
                return conn.Query<KeyValuePair<int, string>>(strSql).ToDictionary(x => x.Key, x => x.Value);
            }
        }

        public List<KeyValuePair<int, string>> AllAuthorLocationPaths()
        {
            using (var conn = _database.OpenConnection())
            {
                const string strSql = "SELECT \"Id\" AS \"Key\", \"Path\" AS \"Value\" FROM \"Authors\" " +
                                      "UNION ALL SELECT \"Id\" AS \"Key\", \"EbookPath\" AS \"Value\" FROM \"Authors\" WHERE \"EbookPath\" IS NOT NULL " +
                                      "UNION ALL SELECT \"Id\" AS \"Key\", \"AudiobookPath\" AS \"Value\" FROM \"Authors\" WHERE \"AudiobookPath\" IS NOT NULL";

                return conn.Query<KeyValuePair<int, string>>(strSql)
                    .Where(x => x.Value.IsNotNullOrWhiteSpace())
                    .ToList();
            }
        }

        public Dictionary<int, List<int>> AllAuthorTags()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS \"Key\", \"Tags\" AS \"Value\" FROM \"Authors\" WHERE \"Tags\" IS NOT NULL";
                return conn.Query<KeyValuePair<int, List<int>>>(strSql).ToDictionary(x => x.Key, x => x.Value);
            }
        }

        public Author GetAuthorByMetadataId(int authorMetadataId)
        {
            return Query(s => s.AuthorMetadataId == authorMetadataId).SingleOrDefault();
        }

        public List<Author> GetAuthorsByMetadataId(IEnumerable<int> authorMetadataIds)
        {
            return Query(s => authorMetadataIds.Contains(s.AuthorMetadataId));
        }
    }
}
