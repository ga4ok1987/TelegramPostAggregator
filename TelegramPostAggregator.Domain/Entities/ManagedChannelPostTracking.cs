using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class ManagedChannelPostTracking : BaseEntity
{
    public Guid ManagedChannelId { get; set; }
    public ManagedChannel ManagedChannel { get; set; } = null!;

    public Guid ManagedChannelSubscriptionId { get; set; }
    public ManagedChannelSubscription ManagedChannelSubscription { get; set; } = null!;

    public Guid PostId { get; set; }
    public TelegramPost Post { get; set; } = null!;

    public long LastDeliveredMessageId { get; set; }
    public DateTimeOffset LastDeliveredAtUtc { get; set; }
    public DateTimeOffset TrackEditsUntilUtc { get; set; }
    public DateTimeOffset? PendingEditedAtUtc { get; set; }
    public DateTimeOffset? LastProcessedEditedAtUtc { get; set; }
}
