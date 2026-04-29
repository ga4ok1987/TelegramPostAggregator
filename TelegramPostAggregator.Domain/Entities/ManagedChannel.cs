using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class ManagedChannel : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public long TelegramChatId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastVerifiedAtUtc { get; set; }
    public DateTimeOffset? LastWriteSucceededAtUtc { get; set; }
    public string? LastWriteError { get; set; }

    public ICollection<ManagedChannelSubscription> SourceSubscriptions { get; set; } = new List<ManagedChannelSubscription>();
}
