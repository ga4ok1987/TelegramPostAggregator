using System.Net;
using System.Net.Http.Json;
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
    private readonly TelegramBotOptions _options = options.Value;

    public async Task SendAsync(string title, string message, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken) || _options.AlertChatId is null)
        {
            return;
        }

        try
        {
            var text = BuildAlertText(title, message, exception);
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

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(nameof(TelegramErrorAlertService));
        var baseUrl = string.IsNullOrWhiteSpace(_options.LocalBotApiBaseUrl)
            ? DefaultBotApiBaseUrl
            : _options.LocalBotApiBaseUrl.TrimEnd('/');

        client.BaseAddress = new Uri($"{baseUrl}/bot{_options.BotToken}/");
        return client;
    }

    private static string BuildAlertText(string title, string message, Exception? exception)
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

        parts.Add(Html(DateTimeOffset.UtcNow.ToString("u")));

        var text = string.Join("\n\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return text.Length <= TelegramMessageLimit ? text : text[..(TelegramMessageLimit - 3)] + "...";
    }

    private static string Html(string value) =>
        WebUtility.HtmlEncode(value);
}
