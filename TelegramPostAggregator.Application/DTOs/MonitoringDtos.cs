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

public sealed record MonitoringDashboardDto(
    string AllowedEmail,
    int RefreshIntervalSeconds,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyCollection<BotStatusDto> Bots);
