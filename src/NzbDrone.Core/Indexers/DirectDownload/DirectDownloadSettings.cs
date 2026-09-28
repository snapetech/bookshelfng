using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.DirectDownload
{
    public class DirectDownloadSettingsValidator : AbstractValidator<DirectDownloadSettings>
    {
        public DirectDownloadSettingsValidator()
        {
            RuleFor(settings => settings)
                .Custom((settings, context) =>
                {
                    var input = settings.Urls ?? Array.Empty<string>();
                    var normalizedUrls = DirectDownloadSettings.NormalizeUrls(input);

                    if (normalizedUrls.Count == 0)
                    {
                        context.AddFailure("'URLs' must contain at least one http:// or https:// URL");
                        return;
                    }

                    var hasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
                    var hasSlowFallback = settings.EnableSlowFallback;

                    if (!hasApiKey && !hasSlowFallback)
                    {
                        context.AddFailure("At least one download method is required: configure an API key or enable the slow-download browser fallback.");
                        return;
                    }

                    foreach (var rawLine in input)
                    {
                        var trimmed = rawLine.Trim();

                        if (trimmed.IsNullOrWhiteSpace())
                        {
                            continue;
                        }

                        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        {
                            context.AddFailure($"'{trimmed}' must be a valid URL that starts with http:// or https://");
                            return;
                        }

                        if (uri.UserInfo.IsNotNullOrWhiteSpace())
                        {
                            context.AddFailure($"'{trimmed}' must not include embedded credentials");
                            return;
                        }
                    }
                });
        }
    }

    public class DirectDownloadSettings : IIndexerSettings, IJsonOnDeserialized
    {
        private static readonly DirectDownloadSettingsValidator Validator = new DirectDownloadSettingsValidator();
        private List<string> _urls = new List<string>();
        private string _apiKey;

        public DirectDownloadSettings()
        {
        }

        [FieldDefinition(0, Type = FieldType.Tag, Label = "URLs", HelpText = "Add one http:// or https:// URL per tag. BookshelfNG tries them in the order entered.")]
        [JsonConverter(typeof(DirectDownloadUrlListConverter))]
        public IEnumerable<string> Urls
        {
            get => _urls;
            set => _urls = NormalizeUrls(value).ToList();
        }

        [FieldDefinition(1, Label = "API Key", Privacy = PrivacyLevel.ApiKey, HelpText = "Optional. Leave blank when the selected source does not require a key.")]
        public string ApiKey
        {
            get => _apiKey;
            set => _apiKey = NormalizeWhitespace(value).IsNullOrWhiteSpace() ? null : NormalizeWhitespace(value);
        }

        [FieldDefinition(2, Label = "Enable Slow-Download Browser Fallback", Type = FieldType.Checkbox, HelpText = "When the API key is absent or its fast-download resolution fails, use a headless browser to attempt slow-download links. Requires Playwright/Chromium in the Docker runtime.")]
        public bool EnableSlowFallback { get; set; }

        public int? EarlyReleaseLimit { get; set; }

        [JsonIgnore]
        public string BaseUrl
        {
            get => NormalizeUrls(Urls).FirstOrDefault() ?? string.Empty;
            set => Urls = new[] { value };
        }

        public void OnDeserialized()
        {
            Urls = _urls;
            ApiKey = _apiKey;
        }

        public NzbDroneValidationResult Validate()
        {
            Urls = _urls;
            ApiKey = _apiKey;
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        public static IReadOnlyList<string> NormalizeUrls(string urls)
        {
            if (urls.IsNullOrWhiteSpace())
            {
                return Array.Empty<string>();
            }

            return NormalizeUrls(urls.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n'));
        }

        public static IReadOnlyList<string> NormalizeUrls(IEnumerable<string> urls)
        {
            if (urls == null)
            {
                return Array.Empty<string>();
            }

            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in urls)
            {
                var trimmed = NormalizeWhitespace(rawLine);

                if (trimmed.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var normalized = trimmed.TrimEnd('/');
                if (normalized.IsNullOrWhiteSpace())
                {
                    continue;
                }

                if (seen.Add(normalized))
                {
                    results.Add(normalized);
                }
            }

            return results;
        }

        private static string NormalizeWhitespace(string value)
        {
            return value?.Trim();
        }
    }

    public sealed class DirectDownloadUrlListConverter : JsonConverter<IEnumerable<string>>
    {
        public override IEnumerable<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return DirectDownloadSettings.NormalizeUrls(reader.GetString());
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Direct download URLs must be a string or a list of strings.");
            }

            var urls = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("Direct download URL entries must be strings.");
                }

                urls.Add(reader.GetString());
            }

            return DirectDownloadSettings.NormalizeUrls(urls);
        }

        public override void Write(Utf8JsonWriter writer, IEnumerable<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var url in DirectDownloadSettings.NormalizeUrls(value))
            {
                writer.WriteStringValue(url);
            }

            writer.WriteEndArray();
        }
    }
}
