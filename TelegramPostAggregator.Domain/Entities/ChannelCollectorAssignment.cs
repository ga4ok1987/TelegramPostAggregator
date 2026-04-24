using TelegramPostAggregator.Domain.Common;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class ChannelCollectorAssignment : BaseEntity
{
    public Guid ChannelId { get; set; }
    public TrackedChannel Channel { get; set; } = null!;

    public Guid CollectorAccountId { get; set; }
    public CollectorAccount CollectorAccount { get; set; } = null!;

    public bool IsPrimary { get; set; } = true;
    public ChannelTrackingStatus Status { get; set; } = ChannelTrackingStatus.PendingSubscription;
    public DateTimeOffset? JoinedAtUtc { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public string? LastError { get; set; }
}
