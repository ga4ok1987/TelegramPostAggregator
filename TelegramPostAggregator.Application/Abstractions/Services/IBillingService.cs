using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IBillingService
{
    Task<SubscriptionUsageDto> GetSubscriptionUsageAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<ChannelTrackingResultDto> CanAddChannelAsync(long telegramUserId, Guid channelId, CancellationToken cancellationToken = default);
    Task<ChannelTrackingResultDto> CanAddManagedChannelAsync(long telegramUserId, long telegramChatId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionPlanDefinitionDto>> ListAvailablePlansAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DonationOptionDto>> ListAvailableDonationOptionsAsync(CancellationToken cancellationToken = default);
    Task<BillingInvoiceResultDto> CreatePlanInvoiceAsync(BillingInvoiceRequestDto request, CancellationToken cancellationToken = default);
    Task<BillingInvoiceResultDto> CreateDonationInvoiceAsync(BillingInvoiceRequestDto request, CancellationToken cancellationToken = default);
    Task<PreCheckoutDecisionDto> ValidatePreCheckoutAsync(TelegramPreCheckoutQueryDto query, CancellationToken cancellationToken = default);
    Task<PaymentProcessingResultDto> ProcessSuccessfulPaymentAsync(long telegramUserId, TelegramSuccessfulPaymentDto payment, CancellationToken cancellationToken = default);
}
