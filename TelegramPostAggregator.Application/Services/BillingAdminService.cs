using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services;

public sealed class BillingAdminService(
    ISubscriptionPlanRepository subscriptionPlanRepository,
    IDonationOptionRepository donationOptionRepository) : IBillingAdminService
{
    public async Task<BillingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var plans = await subscriptionPlanRepository.ListAsync(cancellationToken);
        var donations = await donationOptionRepository.ListAsync(cancellationToken);

        return new BillingSettingsDto(
            plans.OrderBy(x => x.SortOrder).Select(MapPlan).ToArray(),
            donations.OrderBy(x => x.SortOrder).Select(MapDonation).ToArray());
    }

    public async Task<SubscriptionPlanDefinitionDto?> UpdatePlanAsync(Guid planId, string displayName, int channelLimit, int managedChannelLimit, int priceStars, int? durationDays, bool isEnabled, int sortOrder, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var plan = await subscriptionPlanRepository.GetByIdAsync(planId, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        plan.DisplayName = displayName.Trim();
        plan.ChannelLimit = Math.Max(channelLimit, 1);
        plan.ManagedChannelLimit = Math.Max(managedChannelLimit, 1);
        plan.PriceStars = Math.Max(priceStars, 0);
        plan.DurationDays = plan.IsDefaultPlan ? null : 30;
        plan.IsEnabled = plan.IsDefaultPlan || isEnabled;
        plan.SortOrder = sortOrder;
        plan.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await subscriptionPlanRepository.SaveChangesAsync(cancellationToken);
        return MapPlan(plan);
    }

    public async Task<DonationOptionDto?> UpdateDonationAsync(Guid donationId, string displayName, int starsAmount, bool isEnabled, int sortOrder, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var donation = await donationOptionRepository.GetByIdAsync(donationId, cancellationToken);
        if (donation is null)
        {
            return null;
        }

        donation.DisplayName = displayName.Trim();
        donation.StarsAmount = Math.Max(starsAmount, 1);
        donation.IsEnabled = isEnabled;
        donation.SortOrder = sortOrder;
        donation.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await donationOptionRepository.SaveChangesAsync(cancellationToken);
        return MapDonation(donation);
    }

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        await BillingDefaultsSeeder.EnsureAsync(
            subscriptionPlanRepository,
            donationOptionRepository,
            cancellationToken);
    }

    private static SubscriptionPlanDefinitionDto MapPlan(Domain.Entities.SubscriptionPlanDefinition plan) =>
        new(
            plan.Id,
            plan.Code,
            plan.DisplayName,
            plan.ChannelLimit,
            plan.ManagedChannelLimit,
            plan.PriceStars,
            plan.DurationDays,
            plan.IsEnabled,
            plan.IsDefaultPlan,
            plan.SortOrder);

    private static DonationOptionDto MapDonation(Domain.Entities.DonationOption donation) =>
        new(
            donation.Id,
            donation.Code,
            donation.DisplayName,
            donation.StarsAmount,
            donation.IsEnabled,
            donation.SortOrder);
}
