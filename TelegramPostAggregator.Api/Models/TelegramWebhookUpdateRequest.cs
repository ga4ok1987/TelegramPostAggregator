using System.Text.Json.Serialization;

namespace TelegramPostAggregator.Api.Models;

public sealed class TelegramWebhookUpdateRequest
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public TelegramWebhookMessage? Message { get; set; }

    [JsonPropertyName("callback_query")]
    public TelegramWebhookCallbackQuery? CallbackQuery { get; set; }
}

public sealed class TelegramWebhookMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("chat")]
    public TelegramWebhookChat? Chat { get; set; }

    [JsonPropertyName("from")]
    public TelegramWebhookUser? From { get; set; }
}

public sealed class TelegramWebhookChat
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}

public sealed class TelegramWebhookUser
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

public sealed class TelegramWebhookCallbackQuery
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public TelegramWebhookUser? From { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("message")]
    public TelegramWebhookMessage? Message { get; set; }
}
