using System.Collections.Generic;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class ProcessDownloadDecisionsFixture : CoreTest<ProcessDownloadDecisions>
    {
        [Test]
        public async Task should_skip_a_report_that_is_already_in_a_download_client()
        {
            var remoteBook = Builder<RemoteBook>.CreateNew()
                .With(v => v.Books = new List<Book> { new Book { Id = 1 } })
                .With(v => v.Release = Builder<ReleaseInfo>.CreateNew().With(r => r.Title = "Test release").Build())
                .Build();
            var exception = new ExistingTorrentFoundException(
                "Test client",
                new DownloadClientItem { DownloadId = "abcdef123456", Title = "Existing torrent" });

            Mocker.GetMock<IDownloadService>()
                .Setup(v => v.DownloadReport(remoteBook, null))
                .ThrowsAsync(exception);

            var result = await Subject.ProcessDecision(new DownloadDecision(remoteBook), null);

            Assert.That(result, Is.EqualTo(ProcessedDecisionResult.Skipped));
        }
    }
}
