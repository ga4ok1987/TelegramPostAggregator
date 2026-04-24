using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;

namespace TelegramPostAggregator.Application.Services;

public sealed class BotMonitoringService : IBotMonitoringService
{
    private readonly IReadOnlyDictionary<string, IBotStatusProbe> _probes;
    private readonly IOptionsMonitor<BotMonitoringOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSuccessfulChecks = new(StringComparer.OrdinalIgnoreCase);

    public BotMonitoringService(IEnumerable<IBotStatusProbe> probes, IOptionsMonitor<BotMonitoringOptions> optionsMonitor)
    {
        _probes = probes.ToDictionary(probe => probe.ProbeType, StringComparer.OrdinalIgnoreCase);
        _optionsMonitor = optionsMonitor;
    }

    public async Task<MonitoringDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var bots = options.Bots ?? [];

        var statuses = await Task.WhenAll(bots.Select(bot => BuildStatusAsync(bot, cancellationToken)));

        return new MonitoringDashboardDto(
            options.AllowedEmail,
            Math.Max(5, options.RefreshIntervalSeconds),
            DateTimeOffset.UtcNow,
            statuses.OrderBy(status => status.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<BotStatusDto> BuildStatusAsync(BotDefinitionOptions bot, CancellationToken cancellationToken)
    {
        if (!_probes.TryGetValue(bot.ProbeType, out var probe))
        {
            return new BotStatusDto(
                bot.Id,
                bot.DisplayName,
                bot.Description,
                bot.ProbeType,
                bot.Endpoint ?? bot.HeartbeatFilePath,
                bot.Tags,
                BotHealthState.Unknown,
                "Unknown probe type",
                $"Probe type '{bot.ProbeType}' is not registered.",
                DateTimeOffset.UtcNow,
                _lastSuccessfulChecks.TryGetValue(bot.Id, out var lastSuccessAtUtc) ? lastSuccessAtUtc : null,
                null);
        }

        var result = await probe.CheckAsync(bot, cancellationToken);
        var effectiveLastSuccess = result.IsSuccess
            ? result.CheckedAtUtc
            : _lastSuccessfulChecks.TryGetValue(bot.Id, out var cachedLastSuccessAtUtc)
                ? cachedLastSuccessAtUtc
                : result.LastSuccessAtUtc;

        if (result.IsSuccess)
        {
            _lastSuccessfulChecks[bot.Id] = result.CheckedAtUtc;
        }

        return new BotStatusDto(
            bot.Id,
            bot.DisplayName,
            bot.Description,
            bot.ProbeType,
            result.Target ?? bot.Endpoint ?? bot.HeartbeatFilePath,
            bot.Tags,
            result.State,
            result.Summary,
            result.Details,
            result.CheckedAtUtc,
            effectiveLastSuccess,
            result.ResponseTime);
    }
}
