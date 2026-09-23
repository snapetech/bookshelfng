using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public class BookAddedHandler : IHandle<BookAddedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ICheckIfAuthorShouldBeRefreshed _checkIfAuthorShouldBeRefreshed;

        public BookAddedHandler(IManageCommandQueue commandQueueManager,
                                ICheckIfAuthorShouldBeRefreshed checkIfAuthorShouldBeRefreshed)
        {
            _commandQueueManager = commandQueueManager;
            _checkIfAuthorShouldBeRefreshed = checkIfAuthorShouldBeRefreshed;
        }

        public void Handle(BookAddedEvent message)
        {
            if (!message.DoRefresh)
            {
                return;
            }

            var author = message.Book.Author.Value;

            if (!author.LastInfoSync.HasValue || _checkIfAuthorShouldBeRefreshed.ShouldRefresh(author))
            {
                _commandQueueManager.Push(new RefreshAuthorCommand(author.Id));
            }
        }
    }
}
