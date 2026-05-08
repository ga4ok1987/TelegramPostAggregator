using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Services;

internal static class BillingDefaults
{
    public static IReadOnlyList<SubscriptionPlanDefinition> CreatePlans() =>
    [
        new()
        {
            Code = "free",
            DisplayName = "Free",
            ChannelLimit = 10,
            PriceStars = 0,
            DurationDays = null,
            IsEnabled = true,
            IsDefaultPlan = true,
            SortOrder = 10
        },
        new()
        {
            Code = "basic",
            DisplayName = "Basic",
            ChannelLimit = 20,
            PriceStars = 100,
            DurationDays = 30,
            IsEnabled = true,
            SortOrder = 20
        },
        new()
        {
            Code = "pro",
            DisplayName = "Pro",
            ChannelLimit = 50,
            PriceStars = 250,
            DurationDays = 30,
            IsEnabled = true,
            SortOrder = 30
        },
        new()
        {
            Code = "business",
            DisplayName = "Business",
            ChannelLimit = 150,
            PriceStars = 700,
            DurationDays = 30,
            IsEnabled = true,
            SortOrder = 40
        },
        new()
        {
            Code = "business-plus-plus",
            DisplayName = "Business++",
            ChannelLimit = 250,
            PriceStars = 1200,
            DurationDays = 30,
            IsEnabled = true,
            SortOrder = 50
        }
    ];

    public static IReadOnlyList<DonationOption> CreateDonations() =>
    [
        new()
        {
            Code = "donation-25",
            DisplayName = "25 Stars",
            StarsAmount = 25,
            IsEnabled = true,
            SortOrder = 10
        },
        new()
        {
            Code = "donation-50",
            DisplayName = "50 Stars",
            StarsAmount = 50,
            IsEnabled = true,
            SortOrder = 20
        },
        new()
        {
            Code = "donation-100",
            DisplayName = "100 Stars",
            StarsAmount = 100,
            IsEnabled = true,
            SortOrder = 30
        }
    ];
}
