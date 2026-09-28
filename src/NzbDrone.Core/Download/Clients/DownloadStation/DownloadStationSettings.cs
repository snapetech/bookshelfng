using System.Text.RegularExpressions;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.DownloadStation
{
    public class DownloadStationSettingsValidator : AbstractValidator<DownloadStationSettings>
    {
        public DownloadStationSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);

            RuleFor(c => c.TvDirectory).Matches(@"^(?!/).+")
                                       .When(c => c.TvDirectory.IsNotNullOrWhiteSpace())
                                       .WithMessage("Cannot start with /");

            RuleFor(c => c.MusicCategory).Matches(@"^\.?[-a-z]*$", RegexOptions.IgnoreCase).WithMessage("Allowed characters a-z and -");
            RuleFor(c => c.EbookCategory).Matches(@"^\.?[-a-z]*$", RegexOptions.IgnoreCase).WithMessage("Allowed characters a-z and -");
            RuleFor(c => c.AudiobookCategory).Matches(@"^\.?[-a-z]*$", RegexOptions.IgnoreCase).WithMessage("Allowed characters a-z and -");

            RuleFor(c => c.MusicCategory).Empty()
                .When(c => c.TvDirectory.IsNotNullOrWhiteSpace())
                .WithMessage("Cannot use Category and Directory");
            RuleFor(c => c.EbookCategory).Empty()
                .When(c => c.TvDirectory.IsNotNullOrWhiteSpace())
                .WithMessage("Cannot use Category and Directory");
            RuleFor(c => c.AudiobookCategory).Empty()
                .When(c => c.TvDirectory.IsNotNullOrWhiteSpace())
                .WithMessage("Cannot use Category and Directory");
        }
    }

    public class DownloadStationSettings : IProviderConfig, IBookDownloadCategorySettings
    {
        private static readonly DownloadStationSettingsValidator Validator = new DownloadStationSettingsValidator();

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox)]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "Use SSL", Type = FieldType.Checkbox, HelpText = "Use secure connection when connecting to Download Station")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "Username", Type = FieldType.Textbox, Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(4, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(5, Label = "Legacy Category", Type = FieldType.Textbox, Advanced = true, HelpText = "Fallback category used when an ebook or audiobook category is blank.")]
        public string MusicCategory { get; set; }

        [FieldDefinition(6, Label = "Ebook Category", Type = FieldType.Textbox, HelpText = "Category used for ebook downloads. Leave blank to use the legacy category.")]
        public string EbookCategory { get; set; }

        [FieldDefinition(7, Label = "Audiobook Category", Type = FieldType.Textbox, HelpText = "Category used for audiobook downloads. Leave blank to use the legacy category.")]
        public string AudiobookCategory { get; set; }

        [FieldDefinition(8, Label = "Directory", Type = FieldType.Textbox, HelpText = "Optional shared folder to put downloads into, leave blank to use the default Download Station location")]
        public string TvDirectory { get; set; }

        public DownloadStationSettings()
        {
            this.Host = "127.0.0.1";
            this.Port = 5000;
        }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
