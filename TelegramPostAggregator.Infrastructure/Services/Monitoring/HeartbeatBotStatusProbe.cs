using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;

namespace TelegramPostAggregator.Infrastructure.Services.Monitoring;

public sealed class HeartbeatBotStatusProbe : IBotStatusProbe
{
    public string ProbeType => "heartbeat";

    public Task<BotProbeResultDto> CheckAsync(BotDefinitionOptions bot, CancellationToken cancellationToken = default)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;
        var target = bot.HeartbeatFilePath;

        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(new BotProbeResultDto(
                BotHealthState.Unknown,
                "Heartbeat file is not configured",
                "Set HeartbeatFilePath for the heartbeat probe.",
                checkedAtUtc,
                null,
                null,
                target,
                false));
        }

        if (!File.Exists(target))
        {
            return Task.FromResult(new BotProbeResultDto(
                BotHealthState.Offline,
                "Heartbeat file not found",
                $"The bot did not create the heartbeat file '{target}'.",
                checkedAtUtc,
                null,
                null,
                target,
                false));
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(target);
        var age = checkedAtUtc - lastWriteUtc;
        var staleAfter = TimeSpan.FromSeconds(Math.Max(5, bot.StaleAfterSeconds));
        var state = age <= staleAfter ? BotHealthState.Healthy : BotHealthState.Offline;
        var summary = state == BotHealthState.Healthy ? "Heartbeat is fresh" : "Heartbeat is stale";
        var details = $"Last file update was {age.TotalSeconds:F0}s ago at {lastWriteUtc:yyyy-MM-dd HH:mm:ss} UTC.";

        return Task.FromResult(new BotProbeResultDto(
            state,
            summary,
            details,
            checkedAtUtc,
            lastWriteUtc,
            null,
            target,
            state == BotHealthState.Healthy));
    }
}
