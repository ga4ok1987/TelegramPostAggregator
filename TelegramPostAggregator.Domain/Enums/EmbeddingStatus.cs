namespace TelegramPostAggregator.Domain.Enums;

public enum EmbeddingStatus
{
    None = 0,
    Pending = 1,
    Processing = 2,
    Ready = 3,
    Failed = 4,
    PendingRefresh = 5
}
