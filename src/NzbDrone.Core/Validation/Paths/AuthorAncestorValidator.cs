using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Validation.Paths
{
    public class AuthorAncestorValidator : PropertyValidator
    {
        private readonly IAuthorService _authorService;

        public AuthorAncestorValidator(IAuthorService authorService)
        {
            _authorService = authorService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' overlaps with another author's library path";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", context.PropertyValue.ToString());

            var instanceId = (int)((dynamic)context.ParentContext.InstanceToValidate).Id;
            var path = context.PropertyValue.ToString();
            var authorPaths = _authorService.AllAuthorLocationPaths();

            return authorPaths == null || !authorPaths.Any(s => s.Key != instanceId &&
                (path.IsParentPath(s.Value) || s.Value.IsParentPath(path)));
        }
    }
}
