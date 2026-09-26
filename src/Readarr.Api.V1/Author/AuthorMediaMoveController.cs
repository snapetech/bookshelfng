using System;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using Readarr.Api.V1.Commands;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Author
{
    public class AuthorMediaMoveRequestResource : RestResource
    {
        public string Format { get; set; }
        public string DestinationPath { get; set; }
        public string PreviewToken { get; set; }
    }

    [V1ApiController("author/media-move")]
    public class AuthorMediaMoveController : Controller
    {
        private readonly IAuthorMediaMoveService _authorMediaMoveService;
        private readonly IAuthorService _authorService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ResourceValidator<AuthorMediaMoveRequestResource> _validator;

        public AuthorMediaMoveController(IAuthorMediaMoveService authorMediaMoveService,
                                         IAuthorService authorService,
                                         IManageCommandQueue commandQueueManager,
                                         RootFolderValidator rootFolderValidator,
                                         MappedNetworkDriveValidator mappedNetworkDriveValidator,
                                         AuthorPathValidator authorPathValidator,
                                         AuthorAncestorValidator authorAncestorValidator,
                                         RecycleBinValidator recycleBinValidator,
                                         SystemFolderValidator systemFolderValidator)
        {
            _authorMediaMoveService = authorMediaMoveService;
            _authorService = authorService;
            _commandQueueManager = commandQueueManager;

            _validator = new ResourceValidator<AuthorMediaMoveRequestResource>();
            _validator.RuleFor(resource => resource.Id).ValidId();
            _validator.RuleFor(resource => resource.Format)
                      .NotEmpty()
                      .Must(format => string.Equals(format, "ebook", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(format, "audiobook", StringComparison.OrdinalIgnoreCase))
                      .WithMessage("Format must be 'ebook' or 'audiobook'.");
            _validator.RuleFor(resource => resource.DestinationPath)
                      .Cascade(CascadeMode.Stop)
                      .NotEmpty()
                      .IsValidPath()
                      .SetValidator(rootFolderValidator)
                      .SetValidator(mappedNetworkDriveValidator)
                      .SetValidator(authorPathValidator)
                      .SetValidator(authorAncestorValidator)
                      .SetValidator(recycleBinValidator)
                      .SetValidator(systemFolderValidator);
        }

        [HttpPost("preview")]
        public ActionResult<AuthorMediaMovePreview> Preview([FromBody] AuthorMediaMoveRequestResource resource)
        {
            ValidateResource(resource);

            return Ok(_authorMediaMoveService.Preview(resource.Id, resource.Format, resource.DestinationPath));
        }

        [HttpPost("start")]
        public ActionResult<CommandResource> Start([FromBody] AuthorMediaMoveRequestResource resource)
        {
            ValidateResource(resource);

            if (resource.PreviewToken.IsNullOrWhiteSpace())
            {
                throw new BadRequestException("Preview the move before starting it.");
            }

            var preview = _authorMediaMoveService.Preview(resource.Id, resource.Format, resource.DestinationPath);

            if (!string.Equals(resource.PreviewToken, preview.PreviewToken, StringComparison.Ordinal))
            {
                return Conflict(new { message = "The library changed after this preview. Preview the move again.", preview });
            }

            if (!preview.CanMove)
            {
                return Conflict(new { message = "This move has conflicts or no movable files.", preview });
            }

            var author = _authorService.GetAuthor(resource.Id);
            var formatPath = resource.DestinationPath.PathEquals(author.Path) ? null : resource.DestinationPath;
            if (preview.Format == "ebook")
            {
                author.EbookPath = formatPath;
            }
            else
            {
                author.AudiobookPath = formatPath;
            }

            _authorService.UpdateAuthor(author);

            var command = _commandQueueManager.Push(new MoveAuthorMediaCommand
            {
                AuthorId = author.Id,
                Format = preview.Format,
                SourcePath = preview.SourcePath,
                DestinationPath = preview.DestinationPath,
                PreviewToken = preview.PreviewToken,
                Files = preview.Files
            },
                CommandPriority.Normal,
                CommandTrigger.Manual);

            return Accepted(command.ToResource());
        }

        private void ValidateResource(AuthorMediaMoveRequestResource resource)
        {
            if (resource == null)
            {
                throw new BadRequestException("Request body can't be empty");
            }

            var result = _validator.Validate(resource);
            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }
    }
}
