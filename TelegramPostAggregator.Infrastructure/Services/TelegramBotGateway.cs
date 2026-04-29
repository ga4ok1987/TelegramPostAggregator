using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TelegramBotGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramBotOptions> options) : ITelegramBotGateway
{
    private const string DefaultBotApiBaseUrl = "https://api.telegram.org";
    private readonly TelegramBotOptions _options = options.Value;
    private long? _botUserId;

    public async Task<IReadOnlyList<TelegramBotUpdateDto>> GetUpdatesAsync(long offset, CancellationToken cancellationToken = default)
    {
        var url = $"getUpdates?timeout={Math.Max(1, _options.PollingTimeoutSeconds)}&offset={offset}";
        var response = await CreateClient().GetFromJsonAsync<TelegramApiResponse<List<TelegramGetUpdate>>>(url, cancellationToken);

        return response?.Result?
            .Select(MapUpdate)
            .Where(update => update is not null)
            .Cast<TelegramBotUpdateDto>()
            .ToArray() ?? [];
    }

    public Task<TelegramBotApiResultDto> SendMessageAsync(TelegramBotOutboundMessageDto message, CancellationToken cancellationToken = default)
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
            payload["reply_markup"] = CreateReplyMarkup(message.ReplyMarkup);
        }

        return PostJsonAsync("sendMessage", payload, cancellationToken);
    }

    public Task<TelegramBotApiResultDto> SendPhotoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendPhoto", "photo", message, cancellationToken);

    public Task<TelegramBotApiResultDto> SendVideoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendVideo", "video", message, cancellationToken);

    public Task<TelegramBotApiResultDto> SendAudioAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendAudio", "audio", message, cancellationToken);

    public Task<TelegramBotApiResultDto> SendVoiceAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendVoice", "voice", message, cancellationToken);

    public Task<TelegramBotApiResultDto> SendDocumentAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendDocument", "document", message, cancellationToken);

    public Task<TelegramBotApiResultDto> SendAnimationAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendAnimation", "animation", message, cancellationToken);

    public Task<TelegramBotApiResultDto> SendVideoNoteAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendVideoNote", "video_note", message, cancellationToken);

    public async Task<TelegramBotApiResultDto?> SendMediaGroupAsync(TelegramBotMediaGroupMessageDto message, CancellationToken cancellationToken = default)
    {
        if (message.Items.Count == 0)
        {
            return null;
        }

        if (_options.UseLocalBotApiFileTransport && message.Items.All(item => File.Exists(item.FilePath)))
        {
            var mediaPayload = message.Items.Select(item => CreateMediaPayload(item, item.FilePath)).ToArray();

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = message.ChatId.ToString(),
                ["media"] = JsonSerializer.Serialize(mediaPayload)
            });

            using var response = await CreateClient().PostAsync("sendMediaGroup", content, cancellationToken);
            var localResult = await ToResultAsync(response, cancellationToken);
            if (localResult.IsSuccessStatusCode)
            {
                return localResult;
            }
        }

        var streams = new List<Stream>();

        try
        {
            var mediaItems = new List<object>();
            for (var index = 0; index < message.Items.Count; index++)
            {
                var item = message.Items[index];
                if (!File.Exists(item.FilePath))
                {
                    DisposeStreams(streams);
                    return null;
                }

                var attachName = $"file{index}";
                mediaItems.Add(CreateMediaPayload(item, $"attach://{attachName}"));

                streams.Add(File.OpenRead(item.FilePath));
            }

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(message.ChatId.ToString()), "chat_id");
            content.Add(new StringContent(JsonSerializer.Serialize(mediaItems), Encoding.UTF8, "application/json"), "media");

            for (var index = 0; index < streams.Count; index++)
            {
                var fileContent = new StreamContent(streams[index]);
                content.Add(fileContent, $"file{index}", Path.GetFileName(message.Items[index].FilePath));
            }

            using var response = await CreateClient().PostAsync("sendMediaGroup", content, cancellationToken);
            var localResult = await ToResultAsync(response, cancellationToken);
            if (localResult.IsSuccessStatusCode)
            {
                return localResult;
            }
        }
        catch
        {
            DisposeStreams(streams);
            throw;
        }

        return null;
    }

    public Task<TelegramBotApiResultDto> AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken cancellationToken = default) =>
        PostJsonAsync("answerCallbackQuery", new
        {
            callback_query_id = callbackQueryId,
            text = string.IsNullOrWhiteSpace(text) ? "Готово" : text
        }, cancellationToken);

    public async Task<bool> IsBotAdministratorAsync(string telegramChannelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(telegramChannelId))
        {
            return false;
        }

        var botUserId = await GetBotUserIdAsync(cancellationToken);
        if (botUserId is null)
        {
            return false;
        }

        using var response = await CreateClient().PostAsJsonAsync("getChatMember", new
        {
            chat_id = telegramChannelId,
            user_id = botUserId.Value
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!payload.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            return false;
        }

        if (!payload.RootElement.TryGetProperty("result", out var resultElement) ||
            !resultElement.TryGetProperty("status", out var statusElement))
        {
            return false;
        }

        var status = statusElement.GetString();
        return string.Equals(status, "administrator", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "creator", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TelegramBotApiResultDto> SendMediaAsync(
        string endpoint,
        string fieldName,
        TelegramBotMediaMessageDto message,
        CancellationToken cancellationToken)
    {
        if (_options.UseLocalBotApiFileTransport && File.Exists(message.FilePath))
        {
            using var localContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = message.ChatId.ToString(),
                [fieldName] = message.FilePath,
                ["caption"] = message.Caption,
                ["parse_mode"] = message.ParseMode ?? string.Empty
            });

            using var localResponse = await CreateClient().PostAsync(endpoint, localContent, cancellationToken);
            var localResult = await ToResultAsync(localResponse, cancellationToken);
            if (localResult.IsSuccessStatusCode)
            {
                return localResult;
            }
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(message.ChatId.ToString()), "chat_id");
        content.Add(new StringContent(message.Caption, Encoding.UTF8), "caption");
        if (!string.IsNullOrWhiteSpace(message.ParseMode))
        {
            content.Add(new StringContent(message.ParseMode, Encoding.UTF8), "parse_mode");
        }

        using var stream = File.OpenRead(message.FilePath);
        using var fileContent = new StreamContent(stream);
        content.Add(fileContent, fieldName, Path.GetFileName(message.FilePath));

        using var response = await CreateClient().PostAsync(endpoint, content, cancellationToken);
        return await ToResultAsync(response, cancellationToken);
    }

    private async Task<TelegramBotApiResultDto> PostJsonAsync(string endpoint, object payload, CancellationToken cancellationToken)
    {
        using var response = await CreateClient().PostAsJsonAsync(endpoint, payload, cancellationToken);
        return await ToResultAsync(response, cancellationToken);
    }

    private async Task<long?> GetBotUserIdAsync(CancellationToken cancellationToken)
    {
        if (_botUserId.HasValue)
        {
            return _botUserId.Value;
        }

        var response = await CreateClient().GetFromJsonAsync<TelegramApiResponse<TelegramGetUser>>("getMe", cancellationToken);
        if (response?.Ok != true || response.Result is null)
        {
            return null;
        }

        _botUserId = response.Result.Id;
        return _botUserId.Value;
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

    private static async Task<TelegramBotApiResultDto> ToResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = response.IsSuccessStatusCode
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        return new TelegramBotApiResultDto(response.IsSuccessStatusCode, response.StatusCode, body);
    }

    private static Dictionary<string, object?> CreateMediaPayload(TelegramBotMediaGroupItemDto item, string mediaReference)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = item.MediaKind,
            ["media"] = mediaReference
        };

        if (!string.IsNullOrWhiteSpace(item.Caption))
        {
            payload["caption"] = item.Caption;
            if (!string.IsNullOrWhiteSpace(item.ParseMode))
            {
                payload["parse_mode"] = item.ParseMode;
            }
        }

        return payload;
    }

    private static object CreateReplyMarkup(BotReplyMarkupDto replyMarkup)
    {
        if (replyMarkup.IsInline)
        {
            return new
            {
                inline_keyboard = replyMarkup.Buttons.Select(row =>
                    row.Select(button => new
                    {
                        text = button.Text,
                        callback_data = button.CallbackData ?? button.Text
                    }).ToArray()).ToArray()
            };
        }

        return new
        {
            keyboard = replyMarkup.Buttons.Select(row =>
                row.Select(button => button.WebAppUrl is not null
                    ? new Dictionary<string, object?>
                    {
                        ["text"] = button.Text,
                        ["web_app"] = new Dictionary<string, object?>
                        {
                            ["url"] = button.WebAppUrl
                        }
                    }
                    : new Dictionary<string, object?>
                    {
                        ["text"] = button.Text
                    }).ToArray()).ToArray(),
            resize_keyboard = replyMarkup.ResizeKeyboard
        };
    }

    private static TelegramBotUpdateDto? MapUpdate(TelegramGetUpdate update)
    {
        var sourceUser = update.Message?.From ?? update.CallbackQuery?.From;
        var chatId = update.Message?.Chat?.Id ?? update.CallbackQuery?.Message?.Chat?.Id;
        var text = update.Message?.Text;
        var callbackQueryId = update.CallbackQuery?.Id;
        var callbackData = update.CallbackQuery?.Data;

        if (sourceUser is null)
        {
            return null;
        }

        return new TelegramBotUpdateDto(
            update.UpdateId,
            new BotUserSnapshotDto(
                sourceUser.Id,
                sourceUser.Username ?? string.Empty,
                string.Join(' ', new[] { sourceUser.FirstName, sourceUser.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                sourceUser.LanguageCode),
            text,
            callbackQueryId,
            callbackData,
            chatId,
            DateTimeOffset.UtcNow);
    }

    private static void DisposeStreams(IEnumerable<Stream> streams)
    {
        foreach (var stream in streams)
        {
            stream.Dispose();
        }
    }

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

        [JsonPropertyName("callback_query")]
        public TelegramCallbackQuery? CallbackQuery { get; set; }
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

    private sealed class TelegramCallbackQuery
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("from")]
        public TelegramGetUser? From { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }

        [JsonPropertyName("message")]
        public TelegramGetMessage? Message { get; set; }
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
