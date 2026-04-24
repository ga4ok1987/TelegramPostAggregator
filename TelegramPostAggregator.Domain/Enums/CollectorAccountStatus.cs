namespace TelegramPostAggregator.Domain.Enums;

public enum CollectorAccountStatus
{
    PendingAuth = 1,
    Active = 2,
    RateLimited = 3,
    Suspended = 4,
    Disabled = 5
}
