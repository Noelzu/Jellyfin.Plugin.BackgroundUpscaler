using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BackgroundUpscaler.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BackgroundUpscaler.ScheduledTasks;

public sealed class UpscaleArtworkTask : IScheduledTask
{
    private const int SourceWidth = 1920;
    private const int SourceHeight = 1080;
    private const int TargetWidth = 3840;
    private const int TargetHeight = 2160;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private readonly ILogger<UpscaleArtworkTask> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IImageProcessor _imageProcessor;

    public UpscaleArtworkTask(
        ILogger<UpscaleArtworkTask> logger,
        ILibraryManager libraryManager,
        IImageProcessor imageProcessor)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _imageProcessor = imageProcessor;
    }

    public string Name => "Upscale exact 1080p artwork to 4K";
    public string Key => "BackgroundUpscalerExact1080To4K";
    public string Description => "Uses Real-ESRGAN to enhance local artwork that is exactly 1920x1080 and replace it with a validated 3840x2160 result.";
    public string Category => "Background Upscaler";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = DayOfWeek.Sunday,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            _logger.LogWarning("Background Upscaler plugin instance is not available.");
            progress.Report(100);
            return;
        }

        var config = plugin.Configuration;
        if (!config.Enabled)
        {
            _logger.LogInformation("Background Upscaler is disabled in plugin configuration.");
            progress.Report(100);
            return;
        }

        var selectedLibraryIds = config.SelectedLibraryIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (selectedLibraryIds.Length == 0)
        {
            _logger.LogWarning("No libraries are selected. Nothing will be processed.");
            progress.Report(100);
            return;
        }

        ValidateExecutableConfiguration(config);

        var query = new InternalItemsQuery
        {
            AncestorIds = selectedLibraryIds,
            Recursive = true,
            IsVirtualItem = false,
            GroupByPresentationUniqueKey = false,
            IncludeOwnedItems = true,
            IncludeAlternateVersions = true,
            IncludeExtras = true,
            EnableTotalRecordCount = false
        };

        var items = _libraryManager.GetItemList(query);
        _logger.LogInformation(
            "Scanning {ItemCount} items in {LibraryCount} selected libraries for exact {SourceWidth}x{SourceHeight} artwork.",
            items.Count,
            selectedLibraryIds.Length,
            SourceWidth,
            SourceHeight);

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eligible = 0;
        var upscaled = 0;
        var repairedMetadata = 0;
        var failed = 0;
        var unsupported = 0;

        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[itemIndex];
            var itemDirty = false;

            foreach (var imageInfo in item.ImageInfos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsSupportedLocalImage(imageInfo))
                {
                    if (imageInfo.IsLocalFile && !string.IsNullOrWhiteSpace(imageInfo.Path))
                    {
                        unsupported++;
                    }

                    continue;
                }

                var sourcePath = imageInfo.Path;
                if (!File.Exists(sourcePath))
                {
                    _logger.LogDebug("Skipping missing artwork file {Path} for item {ItemName} ({ItemId}).", sourcePath, item.Name, item.Id);
                    continue;
                }

                var firstReference = seenPaths.Add(sourcePath);

                try
                {
                    var dimensions = _imageProcessor.GetImageDimensions(sourcePath);

                    if (dimensions.Width == TargetWidth && dimensions.Height == TargetHeight)
                    {
                        if (imageInfo.Width != TargetWidth || imageInfo.Height != TargetHeight)
                        {
                            ApplyUpdatedImageMetadata(imageInfo, sourcePath);
                            itemDirty = true;
                            repairedMetadata++;
                        }

                        continue;
                    }

                    if (!firstReference)
                    {
                        continue;
                    }

                    if (dimensions.Width != SourceWidth || dimensions.Height != SourceHeight)
                    {
                        continue;
                    }

                    eligible++;
                    _logger.LogInformation(
                        "Upscaling {ImageType} for {ItemName} ({ItemId}): {Path}",
                        imageInfo.Type,
                        item.Name,
                        item.Id,
                        sourcePath);

                    await UpscaleAndReplaceAsync(sourcePath, config, cancellationToken).ConfigureAwait(false);

                    var finalDimensions = _imageProcessor.GetImageDimensions(sourcePath);
                    if (finalDimensions.Width != TargetWidth || finalDimensions.Height != TargetHeight)
                    {
                        throw new InvalidDataException(
                            $"Final image dimensions are {finalDimensions.Width}x{finalDimensions.Height}; expected {TargetWidth}x{TargetHeight}.");
                    }

                    ApplyUpdatedImageMetadata(imageInfo, sourcePath);
                    itemDirty = true;
                    upscaled++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(
                        ex,
                        "Failed to upscale artwork {Path} for {ItemName} ({ItemId}). The task will continue with the next image.",
                        imageInfo.Path,
                        item.Name,
                        item.Id);
                }
            }

            if (itemDirty)
            {
                try
                {
                    await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "The image file was updated but Jellyfin metadata could not be saved for {ItemName} ({ItemId}). A later task run can repair it.", item.Name, item.Id);
                }
            }

            progress.Report(items.Count == 0 ? 100 : ((itemIndex + 1) * 100d / items.Count));
        }

        progress.Report(100);
        _logger.LogInformation(
            "Background artwork upscale finished. Eligible={Eligible}, Upscaled={Upscaled}, MetadataRepaired={MetadataRepaired}, Failed={Failed}, UnsupportedLocalImageReferences={Unsupported}.",
            eligible,
            upscaled,
            repairedMetadata,
            failed,
            unsupported);
    }

    private static bool IsSupportedLocalImage(ItemImageInfo imageInfo)
    {
        if (!imageInfo.IsLocalFile || string.IsNullOrWhiteSpace(imageInfo.Path))
        {
            return false;
        }

        return SupportedExtensions.Contains(Path.GetExtension(imageInfo.Path));
    }

    private static void ApplyUpdatedImageMetadata(ItemImageInfo imageInfo, string sourcePath)
    {
        imageInfo.Width = TargetWidth;
        imageInfo.Height = TargetHeight;
        imageInfo.DateModified = File.GetLastWriteTimeUtc(sourcePath);
        imageInfo.BlurHash = null;
    }

    private static void ValidateExecutableConfiguration(PluginConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.UpscalerExecutable))
        {
            throw new InvalidOperationException("Real-ESRGAN executable is not configured.");
        }

        if ((config.UpscalerExecutable.Contains(Path.DirectorySeparatorChar)
             || config.UpscalerExecutable.Contains(Path.AltDirectorySeparatorChar))
            && !File.Exists(config.UpscalerExecutable))
        {
            throw new FileNotFoundException("Configured Real-ESRGAN executable does not exist.", config.UpscalerExecutable);
        }
    }

    private async Task UpscaleAndReplaceAsync(string sourcePath, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sourcePath);
        var format = GetRealEsrganFormat(extension);
        var directory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException($"Could not determine directory for {sourcePath}.");
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(sourcePath)}.backgroundupscale-{Guid.NewGuid():N}.{format}");

        try
        {
            await RunRealEsrganAsync(sourcePath, tempPath, format, config, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                throw new InvalidDataException("Real-ESRGAN did not create a usable output file.");
            }

            var outputDimensions = _imageProcessor.GetImageDimensions(tempPath);
            if (outputDimensions.Width != TargetWidth || outputDimensions.Height != TargetHeight)
            {
                throw new InvalidDataException(
                    $"Real-ESRGAN output is {outputDimensions.Width}x{outputDimensions.Height}; expected exactly {TargetWidth}x{TargetHeight}. Original file was not replaced.");
            }

            if (config.KeepOriginalBackup)
            {
                var backupPath = sourcePath + ".backgroundupscale-original.bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(sourcePath, backupPath, overwrite: false);
                }
            }

            File.Move(tempPath, sourcePath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task RunRealEsrganAsync(
        string sourcePath,
        string outputPath,
        string format,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = config.UpscalerExecutable.Trim(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add("2");
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(config.ModelName) ? "realesrgan-x4plus" : config.ModelName.Trim());
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(Math.Max(0, config.TileSize).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(format);

        if (!string.IsNullOrWhiteSpace(config.ModelPath))
        {
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(config.ModelPath.Trim());
        }

        if (config.GpuId >= 0)
        {
            startInfo.ArgumentList.Add("-g");
            startInfo.ArgumentList.Add(config.GpuId.ToString(CultureInfo.InvariantCulture));
        }

        if (config.EnableTta)
        {
            startInfo.ArgumentList.Add("-x");
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start Real-ESRGAN.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not terminate Real-ESRGAN after cancellation.");
            }

            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Real-ESRGAN exited with code {process.ExitCode}. stderr: {LimitForLog(stderr)} stdout: {LimitForLog(stdout)}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogDebug("Real-ESRGAN stderr: {Output}", LimitForLog(stderr));
        }
    }

    private static string GetRealEsrganFormat(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "jpg",
            ".png" => "png",
            ".webp" => "webp",
            _ => throw new NotSupportedException($"Unsupported image extension: {extension}")
        };
    }

    private static string LimitForLog(string value)
    {
        const int maxLength = 4000;
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[^maxLength..];
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete temporary upscale file {Path}.", path);
        }
    }
}
