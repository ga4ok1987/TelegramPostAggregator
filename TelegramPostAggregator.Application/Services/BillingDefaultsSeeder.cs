using TelegramPostAggregator.Application.Abstractions.Repositories;

namespace TelegramPostAggregator.Application.Services;

internal static class BillingDefaultsSeeder
{
    public static async Task EnsureAsync(
        ISubscriptionPlanRepository subscriptionPlanRepository,
        IDonationOptionRepository donationOptionRepository,
        CancellationToken cancellationToken)
    {
        await EnsurePlansAsync(subscriptionPlanRepository, cancellationToken);
        await EnsureDonationsAsync(donationOptionRepository, cancellationToken);
    }

    private static async Task EnsurePlansAsync(
        ISubscriptionPlanRepository subscriptionPlanRepository,
        CancellationToken cancellationToken)
    {
        var plans = await subscriptionPlanRepository.ListAsync(cancellationToken);
        if (plans.Count != 0)
        {
            return;
        }

        foreach (var plan in BillingDefaults.CreatePlans())
        {
            await subscriptionPlanRepository.AddAsync(plan, cancellationToken);
        }

        await subscriptionPlanRepository.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureDonationsAsync(
        IDonationOptionRepository donationOptionRepository,
        CancellationToken cancellationToken)
    {
        var donations = await donationOptionRepository.ListAsync(cancellationToken);
        if (donations.Count != 0)
        {
            return;
        }

        foreach (var donation in BillingDefaults.CreateDonations())
        {
            await donationOptionRepository.AddAsync(donation, cancellationToken);
        }

        await donationOptionRepository.SaveChangesAsync(cancellationToken);
    }
}
