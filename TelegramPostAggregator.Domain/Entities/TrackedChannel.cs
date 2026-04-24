using TelegramPostAggregator.Domain.Common;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class TrackedChannel : BaseEntity
{
    public string TelegramChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string UsernameOrInviteLink { get; set; } = string.Empty;
    public string NormalizedChannelKey { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ChannelTrackingStatus Status { get; set; } = ChannelTrackingStatus.PendingSubscription;
    public DateTimeOffset? LastPostCollectedAtUtc { get; set; }
    public DateTimeOffset? LastSubscriptionAttemptAtUtc { get; set; }
    public string? LastCollectorError { get; set; }

    public ICollection<UserChannelSubscription> UserSubscriptions { get; set; } = new List<UserChannelSubscription>();
    public ICollection<ChannelCollectorAssignment> CollectorAssignments { get; set; } = new List<ChannelCollectorAssignment>();
    public ICollection<TelegramPost> Posts { get; set; } = new List<TelegramPost>();
}
