using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public interface IAudiobookM4bMergeService
    {
        AudiobookM4bMergeBatch Prepare(List<ImportDecision<LocalBook>> decisions, DownloadClientItem downloadClientItem);
        void Complete(AudiobookM4bMergeBatch batch, List<ImportResult> importResults, ImportMode importMode, DownloadClientItem downloadClientItem);
    }

    public class AudiobookM4bMergeBatch
    {
        public List<ImportDecision<LocalBook>> Decisions { get; set; }
        public List<AudiobookM4bMerge> Merges { get; set; }
    }

    public class AudiobookM4bMerge
    {
        public string OutputPath { get; set; }
        public List<string> SourcePaths { get; set; }
    }

    public class AudiobookM4bMergeService : IAudiobookM4bMergeService
    {
        private static readonly Regex NumberRegex = new Regex(@"\d+", RegexOptions.Compiled);
        private static readonly Regex TrackNumberRegex = new Regex(@"^\s*(?<number>\d+)", RegexOptions.Compiled);
        private static readonly Regex DiscNumberRegex = new Regex(@"(?:disc|disk|cd)[\s._-]*(?<number>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly IDiskProvider _diskProvider;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly Logger _logger;

        public AudiobookM4bMergeService(IDiskProvider diskProvider, IMakeImportDecision importDecisionMaker, Logger logger)
        {
            _diskProvider = diskProvider;
            _importDecisionMaker = importDecisionMaker;
            _logger = logger;
        }

        public AudiobookM4bMergeBatch Prepare(List<ImportDecision<LocalBook>> decisions, DownloadClientItem downloadClientItem)
        {
            var batch = new AudiobookM4bMergeBatch
            {
                Decisions = decisions,
                Merges = new List<AudiobookM4bMerge>()
            };

            if (!IsEnabled() || downloadClientItem == null)
            {
                return batch;
            }

            var replacements = new List<ImportDecision<LocalBook>>();
            var sourcePaths = new HashSet<string>(StringComparer.Ordinal);
            var groups = decisions
                .Where(IsApprovedAudioTrack)
                .GroupBy(x => new { x.Item.Book.ForeignBookId, x.Item.Edition.ForeignEditionId });

            foreach (var group in groups)
            {
                var tracks = group.ToList();
                if (tracks.Count < 2 || decisions.Any(x => IsRejectedAudioTrackForEdition(x, group.Key.ForeignBookId, group.Key.ForeignEditionId)))
                {
                    continue;
                }

                tracks = tracks
                    .OrderBy(x => GetDiscNumber(x.Item))
                    .ThenBy(x => GetTrackNumber(x.Item))
                    .ThenBy(x => NumberRegex.Replace(x.Item.Path, match => match.Value.PadLeft(9, '0')), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (tracks.Any(x => x.Item.FileTrackInfo == null || x.Item.FileTrackInfo.Duration <= TimeSpan.Zero))
                {
                    _logger.Warn("Skipping M4B merge for {0}: one or more tracks have no readable duration", tracks[0].Item.Book.Title);
                    continue;
                }

                var merge = TryMerge(tracks);
                if (merge == null)
                {
                    continue;
                }

                var seed = tracks[0].Item;
                List<ImportDecision<LocalBook>> mergedDecisions;
                try
                {
                    mergedDecisions = _importDecisionMaker.GetImportDecisions(
                        new List<System.IO.Abstractions.IFileInfo> { _diskProvider.GetFileInfo(merge.OutputPath) },
                        new IdentificationOverrides
                        {
                            Author = seed.Author,
                            Book = seed.Book,
                            Edition = seed.Edition
                        },
                        new ImportDecisionMakerInfo { DownloadClientItem = downloadClientItem },
                        new ImportDecisionMakerConfig
                        {
                            Filter = FilterFilesType.None,
                            NewDownload = true,
                            SingleRelease = true,
                            IncludeExisting = false,
                            AddNewAuthors = false,
                            KeepAllEditions = false
                        });
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Unable to evaluate merged audiobook for {0}; the original files will be imported", seed.Book.Title);
                    TryDeleteFile(merge.OutputPath);
                    continue;
                }

                var mergedDecision = mergedDecisions.SingleOrDefault(x =>
                    string.Equals(x.Item.Path, merge.OutputPath, StringComparison.OrdinalIgnoreCase));

                if (mergedDecision == null || !mergedDecision.Approved)
                {
                    _logger.Warn("Skipping M4B merge for {0}: the merged file did not pass import checks", seed.Book.Title);
                    TryDeleteFile(merge.OutputPath);
                    continue;
                }

                replacements.Add(mergedDecision);
                foreach (var track in tracks)
                {
                    sourcePaths.Add(track.Item.Path);
                }

                batch.Merges.Add(new AudiobookM4bMerge
                {
                    OutputPath = merge.OutputPath,
                    SourcePaths = tracks.Select(x => x.Item.Path).ToList()
                });
                _logger.Info("Prepared chaptered M4B for {0} from {1} files", seed.Book.Title, tracks.Count);
            }

            if (batch.Merges.Count > 0)
            {
                batch.Decisions = decisions.Where(x => !sourcePaths.Contains(x.Item.Path)).Concat(replacements).ToList();
            }

            return batch;
        }

        public void Complete(AudiobookM4bMergeBatch batch, List<ImportResult> importResults, ImportMode importMode, DownloadClientItem downloadClientItem)
        {
            foreach (var merge in batch.Merges)
            {
                var imported = importResults.Any(x =>
                    x.Result == ImportResultType.Imported &&
                    string.Equals(x.ImportDecision.Item.Path, merge.OutputPath, StringComparison.OrdinalIgnoreCase));

                if (imported && !ShouldKeepSourceFiles(importMode, downloadClientItem))
                {
                    foreach (var sourcePath in merge.SourcePaths)
                    {
                        TryDeleteFile(sourcePath);
                    }
                }

                TryDeleteFile(merge.OutputPath);
            }
        }

        private AudiobookM4bMerge TryMerge(List<ImportDecision<LocalBook>> tracks)
        {
            var outputPath = Path.Combine(Path.GetTempPath(), $"bookshelfng-{Guid.NewGuid():N}.m4b");
            var metadataPath = outputPath + ".ffmeta";

            try
            {
                File.WriteAllText(metadataPath, BuildMetadata(tracks), new UTF8Encoding(false));

                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = GetFfmpegPath(),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    var arguments = process.StartInfo.ArgumentList;
                    arguments.Add("-y");
                    arguments.Add("-hide_banner");
                    arguments.Add("-loglevel");
                    arguments.Add("error");

                    var filters = new List<string>();
                    for (var index = 0; index < tracks.Count; index++)
                    {
                        arguments.Add("-i");
                        arguments.Add(Path.GetFullPath(tracks[index].Item.Path));
                        filters.Add($"[{index}:a:0]aresample=44100,aformat=sample_fmts=fltp:channel_layouts=stereo[a{index}]");
                    }

                    var concatInputs = string.Concat(Enumerable.Range(0, tracks.Count).Select(x => $"[a{x}]"));
                    filters.Add($"{concatInputs}concat=n={tracks.Count}:v=0:a=1[aout]");

                    var metadataInput = tracks.Count;
                    arguments.Add("-f");
                    arguments.Add("ffmetadata");
                    arguments.Add("-i");
                    arguments.Add(metadataPath);
                    arguments.Add("-filter_complex");
                    arguments.Add(string.Join(";", filters));
                    arguments.Add("-map");
                    arguments.Add("[aout]");
                    arguments.Add("-map_metadata");
                    arguments.Add(metadataInput.ToString(CultureInfo.InvariantCulture));
                    arguments.Add("-map_chapters");
                    arguments.Add(metadataInput.ToString(CultureInfo.InvariantCulture));
                    arguments.Add("-c:a");
                    arguments.Add("aac");
                    arguments.Add("-b:a");
                    arguments.Add(GetBitrate());
                    arguments.Add("-movflags");
                    arguments.Add("+faststart");
                    arguments.Add(outputPath);

                    process.Start();
                    var standardOutput = process.StandardOutput.ReadToEndAsync();
                    var standardError = process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    Task.WaitAll(standardOutput, standardError);

                    if (process.ExitCode != 0)
                    {
                        _logger.Warn("FFmpeg could not create a chaptered M4B: {0}", TrimOutput(standardError.Result));
                        TryDeleteFile(outputPath);
                        return null;
                    }

                    if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                    {
                        _logger.Warn("FFmpeg reported success but created no M4B output");
                        TryDeleteFile(outputPath);
                        return null;
                    }
                }

                return new AudiobookM4bMerge
                {
                    OutputPath = outputPath,
                    SourcePaths = tracks.Select(x => x.Item.Path).ToList()
                };
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Unable to merge audiobook files; the original files will be imported");
                TryDeleteFile(outputPath);
                return null;
            }
            finally
            {
                TryDeleteFile(metadataPath);
            }
        }

        private static string BuildMetadata(List<ImportDecision<LocalBook>> tracks)
        {
            var book = tracks[0].Item.Book;
            var author = tracks[0].Item.Author;
            var metadata = new StringBuilder();
            metadata.AppendLine(";FFMETADATA1");
            metadata.AppendLine($"title={EscapeMetadata(book.Title)}");
            metadata.AppendLine($"album={EscapeMetadata(book.Title)}");
            metadata.AppendLine($"artist={EscapeMetadata(author?.Name ?? string.Empty)}");

            long start = 0;
            foreach (var track in tracks)
            {
                var duration = Math.Max(1, (long)Math.Round(track.Item.FileTrackInfo.Duration.TotalMilliseconds));
                metadata.AppendLine("[CHAPTER]");
                metadata.AppendLine("TIMEBASE=1/1000");
                metadata.AppendLine($"START={start.ToString(CultureInfo.InvariantCulture)}");
                metadata.AppendLine($"END={(start + duration).ToString(CultureInfo.InvariantCulture)}");
                metadata.AppendLine($"title={EscapeMetadata(GetChapterTitle(track.Item))}");
                start += duration;
            }

            return metadata.ToString();
        }

        private static string EscapeMetadata(string value)
        {
            var escaped = new StringBuilder();
            foreach (var character in value ?? string.Empty)
            {
                if (character == '\r' || character == '\n')
                {
                    escaped.Append(' ');
                    continue;
                }

                if (character == '\\' || character == '=' || character == ';' || character == '#')
                {
                    escaped.Append('\\');
                }

                escaped.Append(character);
            }

            return escaped.ToString();
        }

        private static string GetChapterTitle(LocalBook track)
        {
            if (!string.IsNullOrWhiteSpace(track.FileTrackInfo?.Title))
            {
                return track.FileTrackInfo.Title;
            }

            return Path.GetFileNameWithoutExtension(track.Path);
        }

        private static int GetDiscNumber(LocalBook track)
        {
            if (track.FileTrackInfo?.DiscNumber > 0)
            {
                return track.FileTrackInfo.DiscNumber;
            }

            var match = DiscNumberRegex.Match(track.Path ?? string.Empty);
            return match.Success && int.TryParse(match.Groups["number"].Value, out var number) ? number : 0;
        }

        private static int GetTrackNumber(LocalBook track)
        {
            var taggedTrack = track.FileTrackInfo?.TrackNumbers?.FirstOrDefault() ?? 0;
            if (taggedTrack > 0)
            {
                return taggedTrack;
            }

            var match = TrackNumberRegex.Match(Path.GetFileNameWithoutExtension(track.Path) ?? string.Empty);
            return match.Success && int.TryParse(match.Groups["number"].Value, out var number) ? number : int.MaxValue;
        }

        private static bool IsApprovedAudioTrack(ImportDecision<LocalBook> decision) =>
            decision.Approved &&
            decision.Item.Book != null &&
            !string.IsNullOrWhiteSpace(decision.Item.Book.ForeignBookId) &&
            decision.Item.Edition != null &&
            !string.IsNullOrWhiteSpace(decision.Item.Edition.ForeignEditionId) &&
            MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(decision.Item.Path)) &&
            !Path.GetExtension(decision.Item.Path).Equals(".m4b", StringComparison.OrdinalIgnoreCase);

        private static bool IsRejectedAudioTrackForEdition(ImportDecision<LocalBook> decision, string bookId, string editionId) =>
            !decision.Approved &&
            decision.Item.Book?.ForeignBookId == bookId &&
            decision.Item.Edition?.ForeignEditionId == editionId &&
            MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(decision.Item.Path));

        private static bool IsEnabled()
        {
            var value = Environment.GetEnvironmentVariable("BOOKSHELF_M4B_MERGE");
            return bool.TryParse(value, out var enabled) && enabled;
        }

        private static string GetFfmpegPath()
        {
            var path = Environment.GetEnvironmentVariable("BOOKSHELF_FFMPEG_PATH");
            return string.IsNullOrWhiteSpace(path) ? "ffmpeg" : path;
        }

        private string GetBitrate()
        {
            var value = Environment.GetEnvironmentVariable("BOOKSHELF_M4B_AAC_BITRATE_KBPS");
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bitrate) || bitrate < 48 || bitrate > 320)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _logger.Warn("Ignoring invalid BOOKSHELF_M4B_AAC_BITRATE_KBPS value; using 128 kbps");
                }

                bitrate = 128;
            }

            return $"{bitrate}k";
        }

        private static bool ShouldKeepSourceFiles(ImportMode importMode, DownloadClientItem downloadClientItem) =>
            importMode == ImportMode.Copy ||
            (importMode == ImportMode.Auto && downloadClientItem != null && !downloadClientItem.CanMoveFiles);

        private void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && _diskProvider.FileExists(path))
                {
                    _diskProvider.DeleteFile(path);
                }
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Unable to clean up temporary or merged audiobook file {0}", path);
            }
        }

        private static string TrimOutput(string output)
        {
            const int maxLength = 2000;
            return output?.Length > maxLength ? output.Substring(output.Length - maxLength) : output;
        }
    }
}
