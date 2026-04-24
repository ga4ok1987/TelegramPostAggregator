using TelegramPostAggregator.Domain.Common;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class CollectorAccount : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string ExternalAccountKey { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public CollectorAccountStatus Status { get; set; } = CollectorAccountStatus.PendingAuth;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
    public string? SerializedSession { get; set; }
    public string? LastError { get; set; }

    public ICollection<ChannelCollectorAssignment> ChannelAssignments { get; set; } = new List<ChannelCollectorAssignment>();
}
