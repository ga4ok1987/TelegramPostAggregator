namespace TelegramPostAggregator.Domain.Enums;

public enum ChannelTrackingStatus
{
    PendingSubscription = 1,
    Active = 2,
    Restricted = 3,
    Failed = 4,
    Disabled = 5
}
