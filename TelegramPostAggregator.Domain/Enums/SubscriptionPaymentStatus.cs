namespace TelegramPostAggregator.Domain.Enums;

public enum SubscriptionPaymentStatus
{
    PendingInvoice = 0,
    PreCheckoutApproved = 1,
    Completed = 2,
    Rejected = 3
}
