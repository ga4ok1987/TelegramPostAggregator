using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class AppUser : BaseEntity
{
    public long TelegramUserId { get; set; }
    public string TelegramUsername { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PreferredLanguageCode { get; set; } = "en";
    public bool IsBlockedBot { get; set; }

    public ICollection<UserChannelSubscription> ChannelSubscriptions { get; set; } = new List<UserChannelSubscription>();
    public ICollection<FactCheckRequest> FactCheckRequests { get; set; } = new List<FactCheckRequest>();
}
