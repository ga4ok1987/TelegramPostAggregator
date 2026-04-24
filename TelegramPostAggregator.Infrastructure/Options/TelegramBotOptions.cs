namespace TelegramPostAggregator.Infrastructure.Options;

public sealed class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public string BotToken { get; set; } = string.Empty;
    public string BotUsername { get; set; } = string.Empty;
    public string? LocalBotApiBaseUrl { get; set; }
    public int PollingTimeoutSeconds { get; set; } = 25;
    public int PollingDelayMilliseconds { get; set; } = 1000;
    public int DeliveryDelayMilliseconds { get; set; } = 5000;
    public int DeliveryBatchSize { get; set; } = 20;
    public int DeliveryPostsPerSubscription { get; set; } = 10;
    public long? AlertChatId { get; set; }
}
