using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Infrastructure.Options;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TelegramBotGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramBotOptions> options,
    IOptions<MiniAppOptions> miniAppOptions,
    IMemoryCache memoryCache,
    Microsoft.Extensions.Logging.ILogger<TelegramBotGateway> logger) : ITelegramBotGateway
{
    private const string DefaultBotApiBaseUrl = "https://api.telegram.org";
    private static readonly TimeSpan ChatPhotoCacheDuration = TimeSpan.FromMinutes(20);
    private readonly TelegramBotOptions _options = options.Value;
    private readonly MiniAppOptions _miniAppOptions = miniAppOptions.Value;
    private long? _botUserId;

    public async Task<IReadOnlyList<TelegramBotUpdateDto>> GetUpdatesAsync(long offset, CancellationToken cancellationToken = default)
    {
        var url = $"getUpdates?timeout={Math.Max(1, _options.PollingTimeoutSeconds)}&offset={offset}";
        using var httpResponse = await CreateClient(useLocalBotApi: false).GetAsync(url, cancellationToken);
        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!httpResponse.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Telegram getUpdates failed. StatusCode={StatusCode}, ResponseBody={ResponseBody}",
                (int)httpResponse.StatusCode,
                responseBody);
            throw new HttpRequestException(
                $"Telegram getUpdates failed with {(int)httpResponse.StatusCode}: {responseBody}",
                null,
                httpResponse.StatusCode);
        }

        var response = JsonSerializer.Deserialize<TelegramApiResponse<List<TelegramGetUpdate>>>(responseBody);

        return response?.Result?
            .Select(update => MapUpdate(update, logger))
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

    public Task<TelegramBotApiResultDto> SendInvoiceAsync(TelegramBotInvoiceDto invoice, CancellationToken cancellationToken = default) =>
        PostJsonAsync("sendInvoice", new
        {
            chat_id = invoice.ChatId,
            title = invoice.Title,
            description = invoice.Description,
            payload = invoice.Payload,
            provider_token = string.Empty,
            currency = invoice.Currency,
            prices = new[]
            {
                new
                {
                    label = invoice.PriceLabel,
                    amount = invoice.TotalAmount
                }
            },
            subscription_period = invoice.SubscriptionPeriodSeconds
        }, cancellationToken);

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

    public Task<TelegramBotApiResultDto> SendStickerAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
        SendMediaAsync("sendSticker", "sticker", message, cancellationToken, includeCaption: false);

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

            if (!ShouldRetryWithOfficialBotApi(localResult))
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

            if (!ShouldRetryWithOfficialBotApi(localResult))
            {
                return localResult;
            }

            DisposeStreams(streams);
            streams.Clear();

            for (var index = 0; index < message.Items.Count; index++)
            {
                streams.Add(File.OpenRead(message.Items[index].FilePath));
            }

            using var officialContent = new MultipartFormDataContent();
            officialContent.Add(new StringContent(message.ChatId.ToString()), "chat_id");
            officialContent.Add(new StringContent(JsonSerializer.Serialize(mediaItems), Encoding.UTF8, "application/json"), "media");

            for (var index = 0; index < streams.Count; index++)
            {
                var fileContent = new StreamContent(streams[index]);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(ResolveUploadContentType(message.Items[index].MediaKind, message.Items[index].FilePath));
                officialContent.Add(fileContent, $"file{index}", BuildUploadFileName(message.Items[index].MediaKind, message.Items[index].FilePath));
            }

            using var officialResponse = await CreateClient(useLocalBotApi: false).PostAsync("sendMediaGroup", officialContent, cancellationToken);
            return await ToResultAsync(officialResponse, cancellationToken);
        }
        catch
        {
            DisposeStreams(streams);
            throw;
        }
    }

    public Task<TelegramBotApiResultDto> AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken cancellationToken = default) =>
        PostJsonAsync("answerCallbackQuery", new
        {
            callback_query_id = callbackQueryId,
            text = string.IsNullOrWhiteSpace(text) ? "Готово" : text
        }, cancellationToken);

    public Task<TelegramBotApiResultDto> AnswerPreCheckoutQueryAsync(string preCheckoutQueryId, bool ok, string? errorMessage, CancellationToken cancellationToken = default) =>
        PostJsonAsync("answerPreCheckoutQuery", new
        {
            pre_checkout_query_id = preCheckoutQueryId,
            ok,
            error_message = errorMessage
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

    public Task<TelegramBotApiResultDto> LeaveChatAsync(string telegramChannelId, CancellationToken cancellationToken = default) =>
        PostJsonAsync("leaveChat", new
        {
            chat_id = telegramChannelId
        }, cancellationToken);

    public Task<TelegramBotApiResultDto> SetChatMenuButtonAsync(string text, string webAppUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webAppUrl))
        {
            return Task.FromResult(new TelegramBotApiResultDto(true, HttpStatusCode.OK, null));
        }

        return PostJsonAsync("setChatMenuButton", new
        {
            menu_button = new
            {
                type = "web_app",
                text,
                web_app = new
                {
                    url = webAppUrl
                }
            }
        }, cancellationToken);
    }

    public Task<string?> GetChatProfileImageDataUrlAsync(string telegramChatReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(telegramChatReference))
        {
            return Task.FromResult<string?>(null);
        }

        var normalizedReference = telegramChatReference.Trim();
        return memoryCache.GetOrCreateAsync(
            $"telegram-chat-photo:{normalizedReference}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = ChatPhotoCacheDuration;
                return await GetChatProfileImageDataUrlCoreAsync(normalizedReference, cancellationToken);
            });
    }

    private async Task<TelegramBotApiResultDto> SendMediaAsync(
        string endpoint,
        string fieldName,
        TelegramBotMediaMessageDto message,
        CancellationToken cancellationToken,
        bool includeCaption = true)
    {
        if (_options.UseLocalBotApiFileTransport &&
            File.Exists(message.FilePath) &&
            ShouldUseLocalPathTransport(fieldName, message.FilePath))
        {
            var localPayload = new Dictionary<string, string>
            {
                ["chat_id"] = message.ChatId.ToString(),
                [fieldName] = message.FilePath
            };

            if (includeCaption)
            {
                localPayload["caption"] = message.Caption;
                localPayload["parse_mode"] = message.ParseMode ?? string.Empty;
            }

            using var localContent = new FormUrlEncodedContent(localPayload);

            using var localResponse = await CreateClient().PostAsync(endpoint, localContent, cancellationToken);
            var localResult = await ToResultAsync(localResponse, cancellationToken);
            if (localResult.IsSuccessStatusCode)
            {
                return localResult;
            }

            if (!ShouldRetryWithOfficialBotApi(localResult))
            {
                return localResult;
            }
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(message.ChatId.ToString()), "chat_id");
        if (includeCaption)
        {
            content.Add(new StringContent(message.Caption, Encoding.UTF8), "caption");
            if (!string.IsNullOrWhiteSpace(message.ParseMode))
            {
                content.Add(new StringContent(message.ParseMode, Encoding.UTF8), "parse_mode");
            }
        }

        using var stream = File.OpenRead(message.FilePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(ResolveUploadContentType(fieldName, message.FilePath));
        content.Add(fileContent, fieldName, BuildUploadFileName(fieldName, message.FilePath));

        using var response = await CreateClient().PostAsync(endpoint, content, cancellationToken);
        var result = await ToResultAsync(response, cancellationToken);
        if (result.IsSuccessStatusCode || !ShouldRetryWithOfficialBotApi(result))
        {
            return result;
        }

        using var officialContent = new MultipartFormDataContent();
        officialContent.Add(new StringContent(message.ChatId.ToString()), "chat_id");
        if (includeCaption)
        {
            officialContent.Add(new StringContent(message.Caption, Encoding.UTF8), "caption");
            if (!string.IsNullOrWhiteSpace(message.ParseMode))
            {
                officialContent.Add(new StringContent(message.ParseMode, Encoding.UTF8), "parse_mode");
            }
        }

        using var officialStream = File.OpenRead(message.FilePath);
        using var officialFileContent = new StreamContent(officialStream);
        officialFileContent.Headers.ContentType = new MediaTypeHeaderValue(ResolveUploadContentType(fieldName, message.FilePath));
        officialContent.Add(officialFileContent, fieldName, BuildUploadFileName(fieldName, message.FilePath));

        using var officialResponse = await CreateClient(useLocalBotApi: false).PostAsync(endpoint, officialContent, cancellationToken);
        return await ToResultAsync(officialResponse, cancellationToken);
    }

    private async Task<TelegramBotApiResultDto> PostJsonAsync(string endpoint, object payload, CancellationToken cancellationToken)
    {
        using var response = await CreateClient(useLocalBotApi: false).PostAsJsonAsync(endpoint, payload, cancellationToken);
        return await ToResultAsync(response, cancellationToken);
    }

    private async Task<string?> GetChatProfileImageDataUrlCoreAsync(string telegramChatReference, CancellationToken cancellationToken)
    {
        var photoFileId = await GetChatPhotoFileIdAsync(telegramChatReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(photoFileId))
        {
            return null;
        }

        var filePath = await GetFilePathAsync(photoFileId, cancellationToken);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fileUri = BuildFileUri(filePath);
        using var response = await httpClientFactory.CreateClient(nameof(TelegramBotGateway)).GetAsync(fileUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            mediaType = InferImageMediaType(filePath);
        }

        return $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
    }

    private async Task<string?> GetChatPhotoFileIdAsync(string telegramChatReference, CancellationToken cancellationToken)
    {
        using var response = await CreateClient(useLocalBotApi: false).PostAsJsonAsync("getChat", new
        {
            chat_id = telegramChatReference
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!payload.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            return null;
        }

        if (!payload.RootElement.TryGetProperty("result", out var resultElement) ||
            !resultElement.TryGetProperty("photo", out var photoElement))
        {
            return null;
        }

        if (photoElement.TryGetProperty("small_file_id", out var smallFileIdElement))
        {
            return smallFileIdElement.GetString();
        }

        if (photoElement.TryGetProperty("big_file_id", out var bigFileIdElement))
        {
            return bigFileIdElement.GetString();
        }

        return null;
    }

    private async Task<string?> GetFilePathAsync(string fileId, CancellationToken cancellationToken)
    {
        using var response = await CreateClient(useLocalBotApi: false).PostAsJsonAsync("getFile", new
        {
            file_id = fileId
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!payload.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            return null;
        }

        if (!payload.RootElement.TryGetProperty("result", out var resultElement) ||
            !resultElement.TryGetProperty("file_path", out var filePathElement))
        {
            return null;
        }

        return filePathElement.GetString();
    }

    private async Task<long?> GetBotUserIdAsync(CancellationToken cancellationToken)
    {
        if (_botUserId.HasValue)
        {
            return _botUserId.Value;
        }

        var response = await CreateClient(useLocalBotApi: false).GetFromJsonAsync<TelegramApiResponse<TelegramGetUser>>("getMe", cancellationToken);
        if (response?.Ok != true || response.Result is null)
        {
            return null;
        }

        _botUserId = response.Result.Id;
        return _botUserId.Value;
    }

    private HttpClient CreateClient(bool useLocalBotApi = true)
    {
        var client = httpClientFactory.CreateClient(nameof(TelegramBotGateway));
        client.BaseAddress = new Uri($"{GetBotApiBaseUrl(useLocalBotApi)}/bot{_options.BotToken}/");
        return client;
    }

    private Uri BuildFileUri(string filePath) =>
        new($"{GetBotApiBaseUrl(useLocalBotApi: true)}/file/bot{_options.BotToken}/{filePath.TrimStart('/')}");

    private string GetBotApiBaseUrl(bool useLocalBotApi) =>
        !useLocalBotApi || string.IsNullOrWhiteSpace(_options.LocalBotApiBaseUrl)
            ? DefaultBotApiBaseUrl
            : _options.LocalBotApiBaseUrl.TrimEnd('/');

    private static async Task<TelegramBotApiResultDto> ToResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var primaryMessageId = TryExtractPrimaryMessageId(body);

        return new TelegramBotApiResultDto(
            response.IsSuccessStatusCode,
            response.StatusCode,
            string.IsNullOrWhiteSpace(body) ? null : body,
            primaryMessageId);
    }

    private static long? TryExtractPrimaryMessageId(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("result", out var resultElement))
            {
                return null;
            }

            if (resultElement.ValueKind == JsonValueKind.Object &&
                resultElement.TryGetProperty("message_id", out var objectMessageIdElement) &&
                objectMessageIdElement.TryGetInt64(out var objectMessageId))
            {
                return objectMessageId;
            }

            if (resultElement.ValueKind == JsonValueKind.Array &&
                resultElement.GetArrayLength() > 0 &&
                resultElement[0].TryGetProperty("message_id", out var arrayMessageIdElement) &&
                arrayMessageIdElement.TryGetInt64(out var arrayMessageId))
            {
                return arrayMessageId;
            }
        }
        catch
        {
            // Best-effort extraction only.
        }

        return null;
    }

    private static bool ShouldRetryWithOfficialBotApi(TelegramBotApiResultDto result)
    {
        if (result.IsSuccessStatusCode || string.IsNullOrWhiteSpace(result.ResponseBody))
        {
            return false;
        }

        return result.ResponseBody.Contains("internal Server Error during file upload", StringComparison.OrdinalIgnoreCase);
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
                row.Select(CreateReplyKeyboardButton).ToArray()).ToArray(),
            resize_keyboard = replyMarkup.ResizeKeyboard
        };
    }

    private static Dictionary<string, object?> CreateReplyKeyboardButton(BotButtonDto button)
    {
        if (button.RequestChat is not null)
        {
            return new Dictionary<string, object?>
            {
                ["text"] = button.Text,
                ["request_chat"] = CreateRequestChat(button.RequestChat)
            };
        }

        if (button.WebAppUrl is not null)
        {
            return new Dictionary<string, object?>
            {
                ["text"] = button.Text,
                ["web_app"] = new Dictionary<string, object?>
                {
                    ["url"] = button.WebAppUrl
                }
            };
        }

        return new Dictionary<string, object?>
        {
            ["text"] = button.Text
        };
    }

    private static object CreateRequestChat(BotButtonRequestChatDto requestChat) => new
    {
        request_id = requestChat.RequestId,
        chat_is_channel = requestChat.ChatIsChannel,
        request_title = requestChat.RequestTitle,
        request_username = requestChat.RequestUsername,
        bot_is_member = requestChat.BotIsMember,
        user_administrator_rights = requestChat.UserAdministratorRights is null ? null : CreateAdministratorRights(requestChat.UserAdministratorRights),
        bot_administrator_rights = requestChat.BotAdministratorRights is null ? null : CreateAdministratorRights(requestChat.BotAdministratorRights)
    };

    private static object CreateAdministratorRights(BotChatAdministratorRightsDto rights) => new
    {
        can_manage_chat = rights.CanManageChat,
        can_delete_messages = rights.CanDeleteMessages,
        can_manage_video_chats = rights.CanManageVideoChats,
        can_restrict_members = rights.CanRestrictMembers,
        can_promote_members = rights.CanPromoteMembers,
        can_change_info = rights.CanChangeInfo,
        can_invite_users = rights.CanInviteUsers,
        can_post_messages = rights.CanPostMessages,
        can_edit_messages = rights.CanEditMessages,
        can_pin_messages = rights.CanPinMessages,
        can_post_stories = rights.CanPostStories,
        can_edit_stories = rights.CanEditStories,
        can_delete_stories = rights.CanDeleteStories,
        can_manage_direct_messages = rights.CanManageDirectMessages
    };

    private static TelegramBotUpdateDto? MapUpdate(TelegramGetUpdate update, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (update.ChannelPost?.Chat is not null)
        {
            return new TelegramBotUpdateDto(
                update.UpdateId,
                null,
                update.ChannelPost.Text,
                null,
                null,
                update.ChannelPost.Chat.Id,
                DateTimeOffset.UtcNow,
                null,
                null,
                true);
        }

        var sourceUser = update.Message?.From ?? update.CallbackQuery?.From ?? update.PreCheckoutQuery?.From ?? update.MyChatMember?.From;
        var messageChat = update.Message?.Chat;
        if (sourceUser is null &&
            update.Message?.ChatShared is not null &&
            messageChat is not null &&
            string.Equals(messageChat.Type, "private", StringComparison.OrdinalIgnoreCase))
        {
            sourceUser = new TelegramGetUser
            {
                Id = messageChat.Id,
                Username = messageChat.Username,
                FirstName = messageChat.FirstName,
                LastName = messageChat.LastName
            };
        }

        var chatId = update.Message?.Chat?.Id ?? update.CallbackQuery?.Message?.Chat?.Id ?? update.PreCheckoutQuery?.From?.Id ?? update.MyChatMember?.Chat?.Id;
        var text = update.Message?.Text;
        var callbackQueryId = update.CallbackQuery?.Id;
        var callbackData = update.CallbackQuery?.Data;

        if (update.MyChatMember is not null)
        {
            logger.LogInformation(
                "Received my_chat_member update. UpdateId={UpdateId}, ChatId={ChatId}, OldStatus={OldStatus}, NewStatus={NewStatus}, ChatType={ChatType}",
                update.UpdateId,
                update.MyChatMember.Chat?.Id,
                update.MyChatMember.OldChatMember?.Status,
                update.MyChatMember.NewChatMember?.Status,
                update.MyChatMember.Chat?.Type);
        }

        if (update.Message?.ChatShared is not null)
        {
            logger.LogInformation(
                "Received chat_shared update. UpdateId={UpdateId}, ChatId={ChatId}, SharedChatId={SharedChatId}, HasSourceUser={HasSourceUser}",
                update.UpdateId,
                chatId,
                update.Message.ChatShared.ChatId,
                sourceUser is not null);
        }

        if (sourceUser is null)
        {
            if (update.Message?.ChatShared is not null)
            {
                logger.LogWarning(
                    "Dropping chat_shared update because source user is missing. UpdateId={UpdateId}, ChatId={ChatId}, SharedChatId={SharedChatId}",
                    update.UpdateId,
                    chatId,
                    update.Message.ChatShared.ChatId);
            }

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
            DateTimeOffset.UtcNow,
            update.Message?.ChatShared is null
                ? null
                : new TelegramSharedChatDto(
                    update.Message.ChatShared.RequestId,
                    update.Message.ChatShared.ChatId,
                    update.Message.ChatShared.Title,
                    update.Message.ChatShared.Username),
            update.MyChatMember is null
                ? null
                : new TelegramMyChatMemberDto(
                    update.MyChatMember.Chat?.Id ?? 0,
                    update.MyChatMember.Chat?.Type,
                    update.MyChatMember.Chat?.Title,
                    update.MyChatMember.Chat?.Username,
                    update.MyChatMember.OldChatMember?.Status,
                    update.MyChatMember.NewChatMember?.Status),
            false,
            update.PreCheckoutQuery is null
                ? null
                : new TelegramPreCheckoutQueryDto(
                    update.PreCheckoutQuery.Id,
                    update.PreCheckoutQuery.Currency ?? "XTR",
                    update.PreCheckoutQuery.TotalAmount,
                    update.PreCheckoutQuery.InvoicePayload ?? string.Empty),
            update.Message?.SuccessfulPayment is null
                ? null
                : new TelegramSuccessfulPaymentDto(
                    update.Message.SuccessfulPayment.Currency ?? "XTR",
                    update.Message.SuccessfulPayment.TotalAmount,
                    update.Message.SuccessfulPayment.InvoicePayload ?? string.Empty,
                    update.Message.SuccessfulPayment.TelegramPaymentChargeId ?? string.Empty,
                    update.Message.SuccessfulPayment.SubscriptionExpirationDateUnix.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(update.Message.SuccessfulPayment.SubscriptionExpirationDateUnix.Value)
                        : null,
                    update.Message.SuccessfulPayment.IsRecurring,
                    update.Message.SuccessfulPayment.IsFirstRecurring));
    }

    private static void DisposeStreams(IEnumerable<Stream> streams)
    {
        foreach (var stream in streams)
        {
            stream.Dispose();
        }
    }

    private static string InferImageMediaType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }

    private static bool ShouldUseLocalPathTransport(string fieldName, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return fieldName switch
        {
            "animation" => false,
            "sticker" => false,
            "video" or "video_note" => extension is ".mp4" or ".m4v" or ".mov" or ".webm",
            "audio" => extension is ".mp3" or ".m4a" or ".aac" or ".ogg" or ".wav",
            "voice" => extension is ".ogg" or ".oga" or ".mp3",
            _ => true
        };
    }

    private static string BuildUploadFileName(string fieldName, string filePath)
    {
        var currentExtension = Path.GetExtension(filePath).ToLowerInvariant();
        var currentName = Path.GetFileName(filePath);

        var preferredExtension = fieldName switch
        {
            "photo" => ".jpg",
            "video" or "video_note" => ".mp4",
            "animation" => currentExtension is ".gif" ? ".gif" : ".mp4",
            "audio" => ".mp3",
            "voice" => ".ogg",
            "sticker" => currentExtension switch
            {
                ".webp" => ".webp",
                ".png" => ".png",
                ".webm" => ".webm",
                ".tgs" => ".tgs",
                _ => ".webp"
            },
            "document" => string.IsNullOrWhiteSpace(currentExtension) ? ".bin" : currentExtension,
            _ => string.IsNullOrWhiteSpace(currentExtension) ? ".bin" : currentExtension
        };

        var hasUsableExtension = !string.IsNullOrWhiteSpace(currentExtension) &&
                                 !string.Equals(currentExtension, ".txt", StringComparison.OrdinalIgnoreCase);

        if (hasUsableExtension && !string.IsNullOrWhiteSpace(currentName))
        {
            return currentName;
        }

        return $"{fieldName}{preferredExtension}";
    }

    private static string ResolveUploadContentType(string fieldName, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return fieldName switch
        {
            "photo" => InferImageMediaType(filePath),
            "video" or "video_note" => extension is ".webm" ? "video/webm" : "video/mp4",
            "animation" => extension switch
            {
                ".gif" => "image/gif",
                ".webm" => "video/webm",
                _ => "video/mp4"
            },
            "audio" => extension switch
            {
                ".ogg" or ".oga" => "audio/ogg",
                ".wav" => "audio/wav",
                ".aac" => "audio/aac",
                _ => "audio/mpeg"
            },
            "voice" => "audio/ogg",
            "sticker" => extension switch
            {
                ".webp" => "image/webp",
                ".png" => "image/png",
                ".webm" => "video/webm",
                ".tgs" => "application/x-tgsticker",
                _ => "application/octet-stream"
            },
            "document" => "application/octet-stream",
            _ => "application/octet-stream"
        };
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

        [JsonPropertyName("channel_post")]
        public TelegramGetMessage? ChannelPost { get; set; }

        [JsonPropertyName("callback_query")]
        public TelegramCallbackQuery? CallbackQuery { get; set; }

        [JsonPropertyName("pre_checkout_query")]
        public TelegramPreCheckoutQuery? PreCheckoutQuery { get; set; }

        [JsonPropertyName("my_chat_member")]
        public TelegramMyChatMemberUpdate? MyChatMember { get; set; }
    }

    private sealed class TelegramGetMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("chat")]
        public TelegramGetChat? Chat { get; set; }

        [JsonPropertyName("from")]
        public TelegramGetUser? From { get; set; }

        [JsonPropertyName("chat_shared")]
        public TelegramSharedChatPayload? ChatShared { get; set; }

        [JsonPropertyName("successful_payment")]
        public TelegramSuccessfulPaymentPayload? SuccessfulPayment { get; set; }
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

    private sealed class TelegramPreCheckoutQuery
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("from")]
        public TelegramGetUser? From { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("total_amount")]
        public int TotalAmount { get; set; }

        [JsonPropertyName("invoice_payload")]
        public string? InvoicePayload { get; set; }
    }

    private sealed class TelegramGetChat
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }
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

    private sealed class TelegramSharedChatPayload
    {
        [JsonPropertyName("request_id")]
        public int RequestId { get; set; }

        [JsonPropertyName("chat_id")]
        public long ChatId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }

    private sealed class TelegramSuccessfulPaymentPayload
    {
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("total_amount")]
        public int TotalAmount { get; set; }

        [JsonPropertyName("invoice_payload")]
        public string? InvoicePayload { get; set; }

        [JsonPropertyName("telegram_payment_charge_id")]
        public string? TelegramPaymentChargeId { get; set; }

        [JsonPropertyName("subscription_expiration_date")]
        public long? SubscriptionExpirationDateUnix { get; set; }

        [JsonPropertyName("is_recurring")]
        public bool IsRecurring { get; set; }

        [JsonPropertyName("is_first_recurring")]
        public bool IsFirstRecurring { get; set; }
    }

    private sealed class TelegramMyChatMemberUpdate
    {
        [JsonPropertyName("from")]
        public TelegramGetUser? From { get; set; }

        [JsonPropertyName("chat")]
        public TelegramGetChat? Chat { get; set; }

        [JsonPropertyName("old_chat_member")]
        public TelegramChatMemberState? OldChatMember { get; set; }

        [JsonPropertyName("new_chat_member")]
        public TelegramChatMemberState? NewChatMember { get; set; }
    }

    private sealed class TelegramChatMemberState
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }
}
