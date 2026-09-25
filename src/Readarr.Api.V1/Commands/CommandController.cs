using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Composition;
using NzbDrone.Common.Serializer;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.Http.REST.Attributes;
using NzbDrone.SignalR;
using Readarr.Http;
using Readarr.Http.REST;
using Readarr.Http.Validation;

namespace Readarr.Api.V1.Commands
{
    [V1ApiController]
    public class CommandController : RestControllerWithSignalR<CommandResource, CommandModel>, IHandle<CommandUpdatedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly KnownTypes _knownTypes;
        private readonly Logger _logger;
        private readonly Debouncer _debouncer;
        private readonly Dictionary<int, CommandResource> _pendingUpdates;

        private readonly CommandPriorityComparer _commandPriorityComparer = new CommandPriorityComparer();

        public CommandController(IManageCommandQueue commandQueueManager,
                             IBroadcastSignalRMessage signalRBroadcaster,
                             KnownTypes knownTypes,
                             Logger logger)
            : base(signalRBroadcaster)
        {
            _commandQueueManager = commandQueueManager;
            _knownTypes = knownTypes;
            _logger = logger;

            _debouncer = new Debouncer(SendUpdates, TimeSpan.FromSeconds(0.1));
            _pendingUpdates = new Dictionary<int, CommandResource>();

            PostValidator.RuleFor(c => c.Name).NotBlank();
        }

        protected override CommandResource GetResourceById(int id)
        {
            try
            {
                return _commandQueueManager.Get(id).ToResource();
            }
            catch (ModelNotFoundException)
            {
                _logger.Debug("Command {0} was not found in the active queue or command database; returning 404 (requestId={1})", id, GetRequestId());
                throw;
            }
        }

        [RestPostById]
        public ActionResult<CommandResource> StartCommand(CommandResource commandResource)
        {
            var commandType =
                _knownTypes.GetImplementations(typeof(Command))
                               .Single(c => c.Name.Replace("Command", "")
                                             .Equals(commandResource.Name, StringComparison.InvariantCultureIgnoreCase));

            Request.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(Request.Body);
            var body = reader.ReadToEnd();
            var priority = commandType == typeof(ManualImportCommand)
                ? CommandPriority.High
                : CommandPriority.Normal;

            dynamic command = STJson.Deserialize(body, commandType);

            command.Trigger = CommandTrigger.Manual;
            command.SuppressMessages = !command.SendUpdatesToClient;
            command.SendUpdatesToClient = true;
            command.ClientUserAgent = Request.Headers["User-Agent"];

            var trackedCommand = _commandQueueManager.Push(command, priority, CommandTrigger.Manual);

            _logger.Debug("Manual command accepted: id={0}; name={1}; priority={2}; requestId={3}",
                trackedCommand.Id,
                trackedCommand.Name,
                trackedCommand.Priority,
                GetRequestId());

            return Created(trackedCommand.Id);
        }

        private string GetRequestId()
        {
            if (HttpContext.Items.TryGetValue("ApiRequestSequenceID", out var requestId))
            {
                return Convert.ToString(requestId, CultureInfo.InvariantCulture);
            }

            return HttpContext.TraceIdentifier;
        }

        [HttpGet]
        public List<CommandResource> GetStartedCommands()
        {
            return _commandQueueManager.All()
                .OrderBy(c => c.Status, _commandPriorityComparer)
                .ThenByDescending(c => c.Priority)
                .ToResource();
        }

        [RestDeleteById]
        public void CancelCommand(int id)
        {
            _commandQueueManager.Cancel(id);
        }

        [NonAction]
        public void Handle(CommandUpdatedEvent message)
        {
            if (message.Command.Body.SendUpdatesToClient)
            {
                lock (_pendingUpdates)
                {
                    _pendingUpdates[message.Command.Id] = message.Command.ToResource();
                }

                _debouncer.Execute();
            }
        }

        private void SendUpdates()
        {
            lock (_pendingUpdates)
            {
                var pendingUpdates = _pendingUpdates.Values.ToArray();
                _pendingUpdates.Clear();

                foreach (var pendingUpdate in pendingUpdates)
                {
                    BroadcastResourceChange(ModelAction.Updated, pendingUpdate);

                    if (pendingUpdate.Name == typeof(MessagingCleanupCommand).Name.Replace("Command", "") &&
                        pendingUpdate.Status == CommandStatus.Completed)
                    {
                        BroadcastResourceChange(ModelAction.Sync);
                    }
                }
            }
        }
    }
}
