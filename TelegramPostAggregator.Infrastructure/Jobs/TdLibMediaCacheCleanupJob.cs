using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Jobs;

[DisableConcurrentExecution(1800)]
public sealed class TdLibMediaCacheCleanupJob(
    IOptions<TdLibOptions> options,
    IErrorAlertService errorAlertService,
    ILogger<TdLibMediaCacheCleanupJob> logger)
{
    private readonly TdLibOptions _options = options.Value;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.MediaCacheCleanupEnabled)
        {
            return;
        }

        var rootPath = _options.FilesDirectory;
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        var cutoffUtc = DateTime.UtcNow.AddHours(-Math.Max(1, _options.MediaCacheRetentionHours));
        var deletedFiles = 0;
        long deletedBytes = 0;
        var diskUsageBefore = TryGetDiskUsage(rootPath);

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
            TryDeleteFile(file, ref deletedFiles, ref deletedBytes);
        }

        if (deletedFiles > 0)
        {
            logger.LogInformation(
                "Cleaned TDLib media cache: removed {DeletedFiles} files and freed {DeletedMegabytes:F1} MiB.",
                deletedFiles,
                deletedBytes / 1024d / 1024d);
        }

        var diskUsageAfterRegularCleanup = TryGetDiskUsage(rootPath);
        if (diskUsageAfterRegularCleanup is { UsedPercent: >= 80 } usageAfterRegularCleanup &&
            usageAfterRegularCleanup.UsedPercent >= Math.Clamp(_options.DiskAlertThresholdPercent, 1, 100))
        {
            await errorAlertService.SendAsync(
                "Disk usage warning",
                $"Disk usage is high after regular cleanup.\nUsedPercent: {usageAfterRegularCleanup.UsedPercent:F1}\nAvailableGiB: {usageAfterRegularCleanup.AvailableBytes / 1024d / 1024d / 1024d:F1}\nTdLibFilesDirectory: {rootPath}",
                cancellationToken: cancellationToken);
        }

        if (diskUsageAfterRegularCleanup is not { } currentUsage ||
            currentUsage.UsedPercent < Math.Clamp(_options.DiskEmergencyCleanupThresholdPercent, 1, 100))
        {
            return;
        }

        var emergencyCutoffUtc = DateTime.UtcNow.AddHours(-Math.Max(1, _options.EmergencyMediaCacheMinimumAgeHours));
        var emergencyCandidates = Directory
            .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists && file.LastWriteTimeUtc < emergencyCutoffUtc)
            .OrderBy(file => file.LastWriteTimeUtc)
            .Take(Math.Max(1, _options.EmergencyMediaCacheCleanupBatchSize))
            .ToList();

        var emergencyDeletedFiles = 0;
        long emergencyDeletedBytes = 0;
        var recoveryTargetPercent = Math.Clamp(_options.DiskRecoveryTargetPercent, 1, 100);
        foreach (var file in emergencyCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TryDeleteFile(file, ref emergencyDeletedFiles, ref emergencyDeletedBytes);

            var usageNow = TryGetDiskUsage(rootPath);
            if (usageNow is not null && usageNow.UsedPercent <= recoveryTargetPercent)
            {
                break;
            }
        }

        var diskUsageAfterEmergencyCleanup = TryGetDiskUsage(rootPath);
        if (emergencyDeletedFiles > 0)
        {
            logger.LogWarning(
                "Emergency TDLib media cleanup triggered: removed {DeletedFiles} files and freed {DeletedMegabytes:F1} MiB. Disk used before={UsedBefore:F1}% after={UsedAfter:F1}%.",
                emergencyDeletedFiles,
                emergencyDeletedBytes / 1024d / 1024d,
                diskUsageBefore?.UsedPercent ?? -1,
                diskUsageAfterEmergencyCleanup?.UsedPercent ?? -1);
        }

        await errorAlertService.SendAsync(
            "Emergency disk cleanup executed",
            $"Disk usage reached the emergency threshold and TDLib media cache cleanup was triggered.\nUsedPercentBefore: {diskUsageBefore?.UsedPercent:F1}\nUsedPercentAfter: {diskUsageAfterEmergencyCleanup?.UsedPercent:F1}\nDeletedFiles: {emergencyDeletedFiles}\nFreedMiB: {emergencyDeletedBytes / 1024d / 1024d:F1}\nTdLibFilesDirectory: {rootPath}",
            cancellationToken: cancellationToken);
    }

    private void TryDeleteFile(FileInfo file, ref int deletedFiles, ref long deletedBytes)
    {
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

    private static DiskUsageSnapshot? TryGetDiskUsage(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return null;
            }

            var usedBytes = drive.TotalSize - drive.AvailableFreeSpace;
            var usedPercent = usedBytes * 100d / drive.TotalSize;
            return new DiskUsageSnapshot(drive.TotalSize, drive.AvailableFreeSpace, usedPercent);
        }
        catch
        {
            return null;
        }
    }

    private sealed record DiskUsageSnapshot(long TotalBytes, long AvailableBytes, double UsedPercent);
}
