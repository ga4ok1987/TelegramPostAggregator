using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Jobs;

[DisableConcurrentExecution(1800)]
public sealed class TdLibMediaCacheCleanupJob(
    IOptions<TdLibOptions> options,
    ILogger<TdLibMediaCacheCleanupJob> logger)
{
    private readonly TdLibOptions _options = options.Value;

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.MediaCacheCleanupEnabled)
        {
            return Task.CompletedTask;
        }

        var rootPath = _options.FilesDirectory;
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return Task.CompletedTask;
        }

        var cutoffUtc = DateTime.UtcNow.AddHours(-Math.Max(1, _options.MediaCacheRetentionHours));
        var deletedFiles = 0;
        long deletedBytes = 0;

        var candidates = Directory
            .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists && file.LastWriteTimeUtc < cutoffUtc)
            .OrderBy(file => file.LastWriteTimeUtc)
            .Take(Math.Max(1, _options.MediaCacheCleanupBatchSize))
            .ToList();

        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fileLength = file.Length;
                file.Delete();
                deletedFiles++;
                deletedBytes += fileLength;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Failed to delete cached media file {FilePath}", file.FullName);
            }
        }

        if (deletedFiles > 0)
        {
            logger.LogInformation(
                "Cleaned TDLib media cache: removed {DeletedFiles} files and freed {DeletedMegabytes:F1} MiB.",
                deletedFiles,
                deletedBytes / 1024d / 1024d);
        }

        return Task.CompletedTask;
    }
}
