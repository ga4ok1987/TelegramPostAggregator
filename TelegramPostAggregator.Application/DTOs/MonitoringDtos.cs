namespace TelegramPostAggregator.Application.DTOs;

public enum BotHealthState
{
    Healthy,
    Degraded,
    Offline,
    Unknown
}

public sealed record BotProbeResultDto(
    BotHealthState State,
    string Summary,
    string? Details,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset? LastSuccessAtUtc,
    TimeSpan? ResponseTime,
    string? Target,
    bool IsSuccess);

public sealed record BotStatusDto(
    string Id,
    string DisplayName,
    string Description,
    string ProbeType,
    string? Target,
    IReadOnlyCollection<string> Tags,
    BotHealthState State,
    string Summary,
    string? Details,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset? LastSuccessAtUtc,
    TimeSpan? ResponseTime);

public sealed record ServerMetricPointDto(
    DateTimeOffset CapturedAtUtc,
    double CpuPercent,
    double MemoryPercent,
    double DiskPercent);

public sealed record ServerStatusChartDto(
    DateTimeOffset CapturedAtUtc,
    double CpuPercent,
    double MemoryPercent,
    double DiskPercent,
    double LoadAverage1m,
    string MemorySummary,
    string DiskSummary,
    IReadOnlyCollection<ServerMetricPointDto> History);

public sealed record MonitoringDashboardDto(
    string AllowedEmail,
    int RefreshIntervalSeconds,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyCollection<BotStatusDto> Bots,
    ServerStatusChartDto? ServerStatus = null);
