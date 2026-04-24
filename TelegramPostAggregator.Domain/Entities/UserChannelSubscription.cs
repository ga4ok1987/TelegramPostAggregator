using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class UserChannelSubscription : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public Guid ChannelId { get; set; }
    public TrackedChannel Channel { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public long? LastDeliveredTelegramMessageId { get; set; }
    public DateTimeOffset? LastDeliveredAtUtc { get; set; }
}
