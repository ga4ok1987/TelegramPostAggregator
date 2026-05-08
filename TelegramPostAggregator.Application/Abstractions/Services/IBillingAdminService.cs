using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IBillingAdminService
{
    Task<BillingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionPlanDefinitionDto?> UpdatePlanAsync(Guid planId, string displayName, int channelLimit, int priceStars, int? durationDays, bool isEnabled, int sortOrder, CancellationToken cancellationToken = default);
    Task<DonationOptionDto?> UpdateDonationAsync(Guid donationId, string displayName, int starsAmount, bool isEnabled, int sortOrder, CancellationToken cancellationToken = default);
}
