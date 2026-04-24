using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Infrastructure.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TelegramBotGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramBotOptions> options,
    IErrorAlertService errorAlertService,
    ILogger<TelegramBotGateway> logger) : ITelegramBotGateway
{
    private const string DefaultBotApiBaseUrl = "https://api.telegram.org";
    private readonly TelegramBotOptions _options = options.Value;

    public async Task<IReadOnlyList<TelegramBotUpdateDto>> GetUpdatesAsync(long offset, CancellationToken cancellationToken = default)
    {
        var url = $"getUpdates?timeout={Math.Max(1, _options.PollingTimeoutSeconds)}&offset={offset}";
        var response = await CreateClient().GetFromJsonAsync<TelegramApiResponse<List<TelegramGetUpdate>>>(url, cancellationToken);

        return response?.Result?
            .Where(update => update.Message?.From is not null)
            .Select(MapUpdate)
            .ToArray() ?? [];
    }

    public async Task SendMessageAsync(TelegramBotOutboundMessageDto message, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = message.ChatId,
            ["text"] = message.Text,
            ["disable_web_page_preview"] = message.DisableWebPagePreview
        };

        if (!string.IsNullOrWhiteSpace(message.ParseMode))
        {
            payload["parse_mode"] = message.ParseMode;
        }

        if (message.ReplyMarkup is not null)
        {
            payload["reply_markup"] = new
            {
                keyboard = message.ReplyMarkup.Keyboard,
                resize_keyboard = message.ReplyMarkup.ResizeKeyboard,
                one_time_keyboard = message.ReplyMarkup.OneTimeKeyboard
            };
        }

        using var response = await CreateClient().PostAsJsonAsync("sendMessage", payload, cancellationToken);

        await EnsureSuccessAsync(response, "sendMessage", message.ChatId, null, cancellationToken);
    }

    public Task SendPhotoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendPhoto", "photo", message, cancellationToken);

    public Task SendVideoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendVideo", "video", message, cancellationToken);

    public async Task SendMediaGroupAsync(TelegramBotMediaGroupMessageDto message, CancellationToken cancellationToken = default)
    {
        if (!message.Items.Any())
        {
            return;
        }

        var mediaPayload = message.Items.Select((item, index) =>
        {
            var payload = new Dictionary<string, object?>
            {
                ["type"] = item.Type,
                ["media"] = new Uri(item.FilePath).AbsoluteUri,
                ["caption"] = index == 0 ? item.Caption : string.Empty
            };

            if (index == 0 && !string.IsNullOrWhiteSpace(item.Caption) && !string.IsNullOrWhiteSpace(item.ParseMode))
            {
                payload["parse_mode"] = item.ParseMode;
            }

            return payload;
        }).ToArray();

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = message.ChatId.ToString(),
            ["media"] = JsonSerializer.Serialize(mediaPayload)
        });

        using var response = await CreateClient().PostAsync("sendMediaGroup", content, cancellationToken);
        await EnsureSuccessAsync(response, "sendMediaGroup", message.ChatId, string.Join(", ", message.Items.Select(x => x.FilePath)), cancellationToken);
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(nameof(TelegramBotGateway));
        var baseUrl = string.IsNullOrWhiteSpace(_options.LocalBotApiBaseUrl)
            ? DefaultBotApiBaseUrl
            : _options.LocalBotApiBaseUrl.TrimEnd('/');

        client.BaseAddress = new Uri($"{baseUrl}/bot{_options.BotToken}/");
        return client;
    }

    private async Task SendMediaAsync(
        string endpoint,
        string fieldName,
        TelegramBotMediaMessageDto message,
        CancellationToken cancellationToken)
    {
        if (CanUseLocalFileTransport(message.FilePath))
        {
            var localFileUri = new Uri(message.FilePath).AbsoluteUri;
            var localPayload = new Dictionary<string, string>
            {
                ["chat_id"] = message.ChatId.ToString(),
                [fieldName] = localFileUri,
                ["caption"] = message.Caption
            };

            if (!string.IsNullOrWhiteSpace(message.Caption) && !string.IsNullOrWhiteSpace(message.ParseMode))
            {
                localPayload["parse_mode"] = message.ParseMode;
            }

            using var localContent = new FormUrlEncodedContent(localPayload);
            using var localResponse = await CreateClient().PostAsync(endpoint, localContent, cancellationToken);

            await EnsureSuccessAsync(localResponse, endpoint, message.ChatId, localFileUri, cancellationToken);
            return;
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(message.ChatId.ToString()), "chat_id");
        content.Add(new StringContent(message.Caption, Encoding.UTF8), "caption");
        if (!string.IsNullOrWhiteSpace(message.Caption) && !string.IsNullOrWhiteSpace(message.ParseMode))
        {
            content.Add(new StringContent(message.ParseMode), "parse_mode");
        }

        using var stream = File.OpenRead(message.FilePath);
        using var fileContent = new StreamContent(stream);
        content.Add(fileContent, fieldName, Path.GetFileName(message.FilePath));

        using var response = await CreateClient().PostAsync(endpoint, content, cancellationToken);
        await EnsureSuccessAsync(response, endpoint, message.ChatId, message.FilePath, cancellationToken);
    }

    private bool CanUseLocalFileTransport(string filePath) =>
        !string.IsNullOrWhiteSpace(_options.LocalBotApiBaseUrl)
        && Path.IsPathRooted(filePath)
        && File.Exists(filePath);

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string endpoint,
        long chatId,
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogError(
            "Telegram Bot API call {Endpoint} failed for chat {ChatId}. FilePath: {FilePath}. Status: {StatusCode}. Body: {Body}",
            endpoint,
            chatId,
            filePath,
            (int)response.StatusCode,
            body);

        await errorAlertService.SendAsync(
            "Telegram Bot API call failed",
            $"Endpoint: {endpoint}\nChatId: {chatId}\nFilePath: {filePath ?? "-"}\nStatus: {(int)response.StatusCode}\nBody: {body}",
            null,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static TelegramBotUpdateDto MapUpdate(TelegramGetUpdate update) =>
        new(
            update.UpdateId,
            new BotUserSnapshotDto(
                update.Message!.From!.Id,
                update.Message.From.Username ?? string.Empty,
                string.Join(' ', new[] { update.Message.From.FirstName, update.Message.From.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                update.Message.From.LanguageCode),
            update.Message.Text,
            update.Message.Chat?.Id,
            DateTimeOffset.UtcNow);

    private sealed class TelegramApiResponse<T>
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("result")]
        public T? Result { get; set; }
    }

    private sealed class TelegramGetUpdate
    {
        [JsonPropertyName("update_id")]
        public long UpdateId { get; set; }

        [JsonPropertyName("message")]
        public TelegramGetMessage? Message { get; set; }
    }

    private sealed class TelegramGetMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("chat")]
        public TelegramGetChat? Chat { get; set; }

        [JsonPropertyName("from")]
        public TelegramGetUser? From { get; set; }
    }

    private sealed class TelegramGetChat
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }

    private sealed class TelegramGetUser
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("language_code")]
        public string? LanguageCode { get; set; }
    }
}
