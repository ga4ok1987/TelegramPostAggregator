using System.Collections.Generic;
using System.Globalization;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Monitoring.Web.Services;

public sealed class ServerMetricsProvider : IServerMetricsProvider
{
    private const int MaxHistoryPoints = 48;
    private readonly object _sync = new();
    private readonly Queue<ServerMetricPointDto> _history = new();

    public Task<ServerStatusChartDto> GetServerStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();

        lock (_sync)
        {
            _history.Enqueue(new ServerMetricPointDto(
                snapshot.CapturedAtUtc,
                snapshot.CpuPercent,
                snapshot.MemoryPercent,
                snapshot.DiskPercent));

            while (_history.Count > MaxHistoryPoints)
            {
                _history.Dequeue();
            }

            var history = _history.ToArray();
            return Task.FromResult(snapshot with { History = history });
        }
    }

    private static ServerStatusChartDto CaptureSnapshot()
    {
        var capturedAtUtc = DateTimeOffset.UtcNow;
        var loadAverage1m = ReadLoadAverage1m();
        var cpuPercent = Math.Clamp(loadAverage1m / Math.Max(Environment.ProcessorCount, 1) * 100d, 0d, 100d);

        var (totalMemoryBytes, availableMemoryBytes) = ReadMemoryInfo();
        var usedMemoryBytes = Math.Max(totalMemoryBytes - availableMemoryBytes, 0);
        var memoryPercent = totalMemoryBytes <= 0
            ? 0
            : Math.Clamp((double)usedMemoryBytes / totalMemoryBytes * 100d, 0d, 100d);

        var rootDrive = new DriveInfo("/");
        var totalDiskBytes = rootDrive.TotalSize;
        var usedDiskBytes = totalDiskBytes - rootDrive.AvailableFreeSpace;
        var diskPercent = totalDiskBytes <= 0
            ? 0
            : Math.Clamp((double)usedDiskBytes / totalDiskBytes * 100d, 0d, 100d);

        return new ServerStatusChartDto(
            capturedAtUtc,
            Math.Round(cpuPercent, 1),
            Math.Round(memoryPercent, 1),
            Math.Round(diskPercent, 1),
            Math.Round(loadAverage1m, 2),
            $"{FormatBytes(usedMemoryBytes)} / {FormatBytes(totalMemoryBytes)}",
            $"{FormatBytes(usedDiskBytes)} / {FormatBytes(totalDiskBytes)}",
            Array.Empty<ServerMetricPointDto>());
    }

    private static double ReadLoadAverage1m()
    {
        var text = File.ReadAllText("/proc/loadavg").Trim();
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? 0d
            : double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0d;
    }

    private static (long TotalBytes, long AvailableBytes) ReadMemoryInfo()
    {
        long totalKb = 0;
        long availableKb = 0;

        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                totalKb = ParseMeminfoKilobytes(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                availableKb = ParseMeminfoKilobytes(line);
            }

            if (totalKb > 0 && availableKb > 0)
            {
                break;
            }
        }

        return (totalKb * 1024L, availableKb * 1024L);
    }

    private static long ParseMeminfoKilobytes(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb)
            ? kb
            : 0L;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var suffixIndex = 0;
        while (value >= 1024d && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024d;
            suffixIndex++;
        }

        return $"{value:0.#} {suffixes[suffixIndex]}";
    }
}
