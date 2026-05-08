using TelegramPostAggregator.Domain.Common;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class TelegramPostDelivery : BaseEntity
{
    public Guid PostId { get; set; }
    public TelegramPost Post { get; set; } = null!;

    public int RevisionNumber { get; set; }
    public PostDeliveryDestinationKind DestinationKind { get; set; }
    public long DestinationChatId { get; set; }
    public long DeliveredTelegramMessageId { get; set; }
    public DateTimeOffset DeliveredAtUtc { get; set; }
}
