using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TelegramErrorAlertService(
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramErrorAlertService> logger) : IErrorAlertService
{
    private const int TelegramMessageLimit = 4096;
    private const string DefaultBotApiBaseUrl = "https://api.telegram.org";
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AlertRetention = TimeSpan.FromHours(6);
    private static readonly Regex GuidFingerprintRegex = new(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumberFingerprintRegex = new(@"\b\d{4,}\b", RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<string, TimeSpan> CooldownsByTitle = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
    {
        ["Telegram bot polling failed"] = TimeSpan.FromMinutes(5),
        ["Feed delivery iteration failed"] = TimeSpan.FromMinutes(10),
        ["Failed to deliver posts"] = TimeSpan.FromMinutes(15),
        ["Telegram delivery failed"] = TimeSpan.FromMinutes(20),
        ["Realtime post ingestion failed"] = TimeSpan.FromMinutes(10),
        ["TDLib collector update handling failed"] = TimeSpan.FromMinutes(10),
        ["Failed to fetch recent posts"] = TimeSpan.FromMinutes(20),
        ["Failed to join channel"] = TimeSpan.FromMinutes(30)
    };
    private readonly TelegramBotOptions _options = options.Value;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AlertState> _recentAlerts = [];

    public async Task SendAsync(string title, string message, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken) || _options.AlertChatId is null)
        {
            return;
        }

        if (!TryReserveDelivery(title, message, exception, out var suppressedCount))
        {
            return;
        }

        try
        {
            var text = BuildAlertText(title, message, exception, suppressedCount);
            var payload = new
            {
                chat_id = _options.AlertChatId.Value,
                text,
                parse_mode = "HTML",
                disable_web_page_preview = true
            };

            using var response = await CreateClient().PostAsJsonAsync("sendMessage", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Failed to send Telegram alert. Status: {StatusCode}. Body: {Body}",
                    (int)response.StatusCode,
                    body);
            }
        }
        catch (Exception alertException)
        {
            logger.LogWarning(alertException, "Failed to send Telegram alert.");
        }
    }

    private bool TryReserveDelivery(string title, string message, Exception? exception, out int suppressedCount)
    {
        var now = DateTimeOffset.UtcNow;
        var fingerprint = BuildFingerprint(title, message, exception);
        var cooldown = ResolveCooldown(title);

        lock (_gate)
        {
            CleanupExpiredEntries(now);

            if (_recentAlerts.TryGetValue(fingerprint, out var existing) && existing.NextAllowedAtUtc > now)
            {
                _recentAlerts[fingerprint] = existing with
                {
                    LastObservedAtUtc = now,
                    SuppressedCount = existing.SuppressedCount + 1
                };
                suppressedCount = 0;
                return false;
            }

            suppressedCount = existing?.SuppressedCount ?? 0;
            _recentAlerts[fingerprint] = new AlertState(now.Add(cooldown), now, 0);
            return true;
        }
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(nameof(TelegramErrorAlertService));
        var baseUrl = string.IsNullOrWhiteSpace(_options.LocalBotApiBaseUrl)
            ? DefaultBotApiBaseUrl
            : _options.LocalBotApiBaseUrl.TrimEnd('/');

        client.BaseAddress = new Uri($"{baseUrl}/bot{_options.BotToken}/");
        return client;
    }

    private static string BuildAlertText(string title, string message, Exception? exception, int suppressedCount)
    {
        var parts = new List<string>
        {
            $"<b>Channels Monitor alert</b>",
            $"<b>{Html(title)}</b>",
            Html(message)
        };

        if (exception is not null)
        {
            parts.Add($"<pre>{Html(exception.GetType().Name + ": " + exception.Message)}</pre>");
        }

        if (suppressedCount > 0)
        {
            parts.Add(Html($"Suppressed repeats: {suppressedCount}"));
        }

        parts.Add(Html(DateTimeOffset.UtcNow.ToString("u")));

        var text = string.Join("\n\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return text.Length <= TelegramMessageLimit ? text : text[..(TelegramMessageLimit - 3)] + "...";
    }

    private static TimeSpan ResolveCooldown(string title) =>
        CooldownsByTitle.TryGetValue(title, out var cooldown)
            ? cooldown
            : DefaultCooldown;

    private void CleanupExpiredEntries(DateTimeOffset now)
    {
        if (_recentAlerts.Count < 512)
        {
            return;
        }

        foreach (var entry in _recentAlerts)
        {
            if (now - entry.Value.LastObservedAtUtc > AlertRetention)
            {
                _recentAlerts.Remove(entry.Key);
            }
        }
    }

    private static string BuildFingerprint(string title, string message, Exception? exception)
    {
        var normalizedMessage = NormalizeFingerprintValue(message);
        var normalizedException = exception is null
            ? string.Empty
            : NormalizeFingerprintValue($"{exception.GetType().FullName}:{exception.Message}");

        return $"{title.Trim()}\n{normalizedMessage.Trim()}\n{normalizedException.Trim()}";
    }

    private static string NormalizeFingerprintValue(string value)
    {
        var normalized = GuidFingerprintRegex.Replace(value, "{guid}");
        normalized = NumberFingerprintRegex.Replace(normalized, "#");
        return normalized;
    }

    private static string Html(string value) =>
        WebUtility.HtmlEncode(value);

    private sealed record AlertState(
        DateTimeOffset NextAllowedAtUtc,
        DateTimeOffset LastObservedAtUtc,
        int SuppressedCount);
}
