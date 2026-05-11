using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class AppUser : BaseEntity
{
    public long TelegramUserId { get; set; }
    public string TelegramUsername { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PreferredLanguageCode { get; set; } = "en";
    public bool IsBlockedBot { get; set; }
    public string SubscriptionPlanCode { get; set; } = "free";
    public DateTimeOffset? SubscriptionExpiresAtUtc { get; set; }
    public DateTimeOffset? LastStarsPaymentAtUtc { get; set; }
    public int ExtraSubscriptionSlots { get; set; }
    public int ExtraManagedChannelSlots { get; set; }

    public ICollection<UserChannelSubscription> ChannelSubscriptions { get; set; } = new List<UserChannelSubscription>();
    public ICollection<ManagedChannel> ManagedChannels { get; set; } = new List<ManagedChannel>();
    public ICollection<FactCheckRequest> FactCheckRequests { get; set; } = new List<FactCheckRequest>();
    public ICollection<SubscriptionPaymentTransaction> PaymentTransactions { get; set; } = new List<SubscriptionPaymentTransaction>();
}
