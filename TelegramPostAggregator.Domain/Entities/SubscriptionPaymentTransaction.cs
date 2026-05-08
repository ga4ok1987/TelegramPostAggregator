using TelegramPostAggregator.Domain.Common;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class SubscriptionPaymentTransaction : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public SubscriptionPaymentType Type { get; set; }
    public SubscriptionPaymentStatus Status { get; set; }
    public string PayloadToken { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "XTR";
    public string? PlanCode { get; set; }
    public string? DonationCode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TotalAmountStars { get; set; }
    public string? TelegramPaymentChargeId { get; set; }
    public DateTimeOffset? PreCheckoutApprovedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? FailureReason { get; set; }
}
