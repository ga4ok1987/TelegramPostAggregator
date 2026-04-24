using System.Diagnostics;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;

namespace TelegramPostAggregator.Infrastructure.Services.Monitoring;

public sealed class HttpBotStatusProbe : IBotStatusProbe
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpBotStatusProbe(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string ProbeType => "http";

    public async Task<BotProbeResultDto> CheckAsync(BotDefinitionOptions bot, CancellationToken cancellationToken = default)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;
        var target = bot.Endpoint;

        if (string.IsNullOrWhiteSpace(target))
        {
            return new BotProbeResultDto(
                BotHealthState.Unknown,
                "Endpoint is not configured",
                "Set Endpoint for the http probe.",
                checkedAtUtc,
                null,
                null,
                target,
                false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, bot.TimeoutSeconds)));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(HttpBotStatusProbe));
            using var response = await client.GetAsync(target, timeoutCts.Token);
            stopwatch.Stop();

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var details = string.IsNullOrWhiteSpace(body)
                ? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                : TrimToSingleLine(body, 180);

            var state = response.IsSuccessStatusCode ? BotHealthState.Healthy : BotHealthState.Degraded;
            var summary = response.IsSuccessStatusCode ? "Endpoint responded successfully" : "Endpoint returned a non-success status";

            return new BotProbeResultDto(
                state,
                summary,
                details,
                checkedAtUtc,
                response.IsSuccessStatusCode ? checkedAtUtc : null,
                stopwatch.Elapsed,
                target,
                response.IsSuccessStatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            return new BotProbeResultDto(
                BotHealthState.Offline,
                "Endpoint timed out",
                $"The endpoint did not respond within {Math.Max(1, bot.TimeoutSeconds)} second(s).",
                checkedAtUtc,
                null,
                stopwatch.Elapsed,
                target,
                false);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            return new BotProbeResultDto(
                BotHealthState.Offline,
                "Endpoint check failed",
                exception.Message,
                checkedAtUtc,
                null,
                stopwatch.Elapsed,
                target,
                false);
        }
    }

    private static string TrimToSingleLine(string value, int maxLength)
    {
        var singleLine = string.Join(" ", value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.Length <= maxLength ? singleLine : $"{singleLine[..maxLength]}...";
    }
}
