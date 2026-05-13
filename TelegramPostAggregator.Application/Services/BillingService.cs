using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Application.Services;

public sealed class BillingService(
    IAppUserRepository appUserRepository,
    IManagedChannelRepository managedChannelRepository,
    ISubscriptionRepository subscriptionRepository,
    IManagedChannelSubscriptionRepository managedChannelSubscriptionRepository,
    ISubscriptionPlanRepository subscriptionPlanRepository,
    IDonationOptionRepository donationOptionRepository,
    ISubscriptionPaymentTransactionRepository subscriptionPaymentTransactionRepository,
    IUserService userService) : IBillingService
{
    private const int TelegramStarsSubscriptionPeriodSeconds = 30 * 24 * 60 * 60;

    public async Task<SubscriptionUsageDto> GetSubscriptionUsageAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var user = await appUserRepository.GetByTelegramUserIdAsync(telegramUserId, cancellationToken);
        var plan = await ResolveEffectivePlanAsync(user, cancellationToken);
        var usedChannels = user is null ? 0 : await CountUsedUniqueChannelsAsync(user.TelegramUserId, cancellationToken);
        var managedChannels = user is null
            ? []
            : await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var effectiveManagedChannelLimit = Math.Max(plan.ManagedChannelLimit + Math.Max(user?.ExtraManagedChannelSlots ?? 0, 0), 1);

        if (user is not null)
        {
            await PauseOverflowManagedChannelsAsync(telegramUserId, effectiveManagedChannelLimit, cancellationToken);
            managedChannels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        }

        var usedManagedChannels = CountUsedManagedChannels(managedChannels);

        return MapUsage(
            plan,
            usedChannels,
            usedManagedChannels,
            user?.SubscriptionExpiresAtUtc,
            user?.ExtraSubscriptionSlots ?? 0,
            user?.ExtraManagedChannelSlots ?? 0);
    }

    public async Task<ChannelTrackingResultDto> CanAddChannelAsync(long telegramUserId, Guid channelId, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var directSubscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var managedSubscriptions = await managedChannelSubscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        if (directSubscriptions.Any(x => x.ChannelId == channelId) || managedSubscriptions.Any(x => x.ChannelId == channelId))
        {
            var currentUsage = await GetSubscriptionUsageAsync(telegramUserId, cancellationToken);
            return new ChannelTrackingResultDto(true, string.Empty, Usage: currentUsage);
        }

        var usage = await GetSubscriptionUsageAsync(telegramUserId, cancellationToken);
        if (usage.UsedChannels < usage.ChannelLimit)
        {
            return new ChannelTrackingResultDto(true, string.Empty, Usage: usage);
        }

        var message = $"You reached the limit for the {usage.CurrentPlanName} plan: {usage.UsedChannels}/{usage.ChannelLimit} channels. Open Plans to upgrade.";
        return new ChannelTrackingResultDto(false, message, Usage: usage);
    }

    public async Task<ChannelTrackingResultDto> CanAddManagedChannelAsync(long telegramUserId, long telegramChatId, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var user = await appUserRepository.GetByTelegramUserIdAsync(telegramUserId, cancellationToken);
        var usage = await GetSubscriptionUsageAsync(telegramUserId, cancellationToken);
        if (user is null)
        {
            return new ChannelTrackingResultDto(false, "Start the bot first, then connect your channel.", Usage: usage);
        }

        var existingManagedChannels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        if (existingManagedChannels.Any(x => x.TelegramChatId == telegramChatId))
        {
            return new ChannelTrackingResultDto(true, string.Empty, Usage: usage);
        }

        var registrationLimit = await GetManagedChannelRegistrationLimitAsync(user, cancellationToken);
        if (existingManagedChannels.Count < registrationLimit)
        {
            return new ChannelTrackingResultDto(true, string.Empty, Usage: usage);
        }

        var message = $"You reached the connection limit for owned channels: {existingManagedChannels.Count}/{registrationLimit}. Remove an unused channel or ask the admin for extra owned channel slots.";
        return new ChannelTrackingResultDto(false, message, Usage: usage);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDefinitionDto>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var plans = await subscriptionPlanRepository.ListAsync(cancellationToken);
        return plans
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.SortOrder)
            .Select(MapPlan)
            .ToArray();
    }

    public async Task<IReadOnlyList<DonationOptionDto>> ListAvailableDonationOptionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var donations = await donationOptionRepository.ListAsync(cancellationToken);
        return donations
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.SortOrder)
            .Select(MapDonation)
            .ToArray();
    }

    public async Task<BillingInvoiceResultDto> CreatePlanInvoiceAsync(BillingInvoiceRequestDto request, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var user = await userService.UpsertTelegramUserAsync(
            new BotUserSnapshotDto(request.TelegramUserId, request.TelegramUsername, request.DisplayName, "en"),
            cancellationToken);

        var plan = await subscriptionPlanRepository.GetByCodeAsync(request.ProductCode, cancellationToken);
        if (plan is null || !plan.IsEnabled)
        {
            return new BillingInvoiceResultDto(false, "This plan is unavailable right now.");
        }

        if (plan.IsDefaultPlan || plan.PriceStars <= 0)
        {
            return new BillingInvoiceResultDto(false, "The Free plan is already available by default.");
        }

        var transaction = new SubscriptionPaymentTransaction
        {
            UserId = user.Id,
            Type = SubscriptionPaymentType.PlanPurchase,
            Status = SubscriptionPaymentStatus.PendingInvoice,
            PayloadToken = $"subpay:{Guid.NewGuid():N}",
            PlanCode = plan.Code,
            Title = $"{plan.DisplayName} plan",
            Description = BuildPlanDescription(plan),
            TotalAmountStars = plan.PriceStars
        };

        await subscriptionPaymentTransactionRepository.AddAsync(transaction, cancellationToken);
        await subscriptionPaymentTransactionRepository.SaveChangesAsync(cancellationToken);

        return new BillingInvoiceResultDto(
            true,
            $"Subscription checkout created for {plan.DisplayName}.",
            new TelegramBotInvoiceDto(
                request.TelegramUserId,
                transaction.Title,
                transaction.Description,
                transaction.PayloadToken,
                plan.DisplayName,
                plan.PriceStars,
                SubscriptionPeriodSeconds: TelegramStarsSubscriptionPeriodSeconds));
    }

    public async Task<BillingInvoiceResultDto> CreateDonationInvoiceAsync(BillingInvoiceRequestDto request, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var user = await userService.UpsertTelegramUserAsync(
            new BotUserSnapshotDto(request.TelegramUserId, request.TelegramUsername, request.DisplayName, "en"),
            cancellationToken);

        var donation = await donationOptionRepository.GetByCodeAsync(request.ProductCode, cancellationToken);
        if (donation is null || !donation.IsEnabled)
        {
            return new BillingInvoiceResultDto(false, "This donation option is unavailable right now.");
        }

        var transaction = new SubscriptionPaymentTransaction
        {
            UserId = user.Id,
            Type = SubscriptionPaymentType.Donation,
            Status = SubscriptionPaymentStatus.PendingInvoice,
            PayloadToken = $"subpay:{Guid.NewGuid():N}",
            DonationCode = donation.Code,
            Title = "Support the project",
            Description = "Thank you for supporting Channels Monitor.",
            TotalAmountStars = donation.StarsAmount
        };

        await subscriptionPaymentTransactionRepository.AddAsync(transaction, cancellationToken);
        await subscriptionPaymentTransactionRepository.SaveChangesAsync(cancellationToken);

        return new BillingInvoiceResultDto(
            true,
            "Donation invoice created.",
            new TelegramBotInvoiceDto(
                request.TelegramUserId,
                transaction.Title,
                transaction.Description,
                transaction.PayloadToken,
                donation.DisplayName,
                donation.StarsAmount));
    }

    public async Task<PreCheckoutDecisionDto> ValidatePreCheckoutAsync(TelegramPreCheckoutQueryDto query, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var transaction = await subscriptionPaymentTransactionRepository.GetByPayloadTokenAsync(query.InvoicePayload, cancellationToken);
        if (transaction is null)
        {
            return new PreCheckoutDecisionDto(false, "Payment request was not found.");
        }

        if (!string.Equals(query.Currency, "XTR", StringComparison.OrdinalIgnoreCase))
        {
            transaction.Status = SubscriptionPaymentStatus.Rejected;
            transaction.FailureReason = $"Unexpected currency: {query.Currency}";
            transaction.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await subscriptionPaymentTransactionRepository.SaveChangesAsync(cancellationToken);
            return new PreCheckoutDecisionDto(false, "Only Telegram Stars payments are supported.");
        }

        if (transaction.TotalAmountStars != query.TotalAmount)
        {
            transaction.Status = SubscriptionPaymentStatus.Rejected;
            transaction.FailureReason = $"Unexpected amount: {query.TotalAmount}";
            transaction.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await subscriptionPaymentTransactionRepository.SaveChangesAsync(cancellationToken);
            return new PreCheckoutDecisionDto(false, "The invoice amount no longer matches the current price.");
        }

        transaction.Status = SubscriptionPaymentStatus.PreCheckoutApproved;
        transaction.PreCheckoutApprovedAtUtc = DateTimeOffset.UtcNow;
        transaction.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await subscriptionPaymentTransactionRepository.SaveChangesAsync(cancellationToken);
        return new PreCheckoutDecisionDto(true);
    }

    public async Task<PaymentProcessingResultDto> ProcessSuccessfulPaymentAsync(long telegramUserId, TelegramSuccessfulPaymentDto payment, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var existingByCharge = await subscriptionPaymentTransactionRepository.GetByTelegramChargeIdAsync(payment.TelegramPaymentChargeId, cancellationToken);
        if (existingByCharge is not null)
        {
            return new PaymentProcessingResultDto(true, "Payment already processed.");
        }

        var transaction = await subscriptionPaymentTransactionRepository.GetByPayloadTokenAsync(payment.InvoicePayload, cancellationToken);
        if (transaction is null)
        {
            return new PaymentProcessingResultDto(false, "Payment completed, but the invoice payload was not recognized.");
        }

        var user = await appUserRepository.GetByTelegramUserIdAsync(telegramUserId, cancellationToken);
        if (user is null)
        {
            return new PaymentProcessingResultDto(false, "Payment completed, but the user could not be found.");
        }

        if (transaction.Type == SubscriptionPaymentType.PlanPurchase)
        {
            var plan = await subscriptionPlanRepository.GetByCodeAsync(transaction.PlanCode ?? string.Empty, cancellationToken);
            if (plan is null)
            {
                return new PaymentProcessingResultDto(false, "Payment completed, but the selected plan no longer exists.");
            }

            user.SubscriptionPlanCode = plan.Code;
            user.SubscriptionExpiresAtUtc = payment.SubscriptionExpirationDate ?? DateTimeOffset.UtcNow.AddSeconds(TelegramStarsSubscriptionPeriodSeconds);
            user.LastStarsPaymentAtUtc = DateTimeOffset.UtcNow;
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            user.LastStarsPaymentAtUtc = DateTimeOffset.UtcNow;
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        transaction.Status = SubscriptionPaymentStatus.Completed;
        transaction.TelegramPaymentChargeId ??= payment.TelegramPaymentChargeId;
        transaction.CompletedAtUtc = DateTimeOffset.UtcNow;
        transaction.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await appUserRepository.SaveChangesAsync(cancellationToken);
        await subscriptionPaymentTransactionRepository.SaveChangesAsync(cancellationToken);

        return transaction.Type == SubscriptionPaymentType.PlanPurchase
            ? new PaymentProcessingResultDto(
                true,
                payment.IsRecurring && !payment.IsFirstRecurring
                    ? $"Subscription renewed: {transaction.Title}."
                    : $"Subscription activated: {transaction.Title}.")
            : new PaymentProcessingResultDto(true, "Thank you for supporting the project.");
    }

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        await BillingDefaultsSeeder.EnsureAsync(
            subscriptionPlanRepository,
            donationOptionRepository,
            cancellationToken);
    }

    private async Task<SubscriptionPlanDefinition> ResolveEffectivePlanAsync(AppUser? user, CancellationToken cancellationToken)
    {
        var defaultPlan = await subscriptionPlanRepository.GetByCodeAsync("free", cancellationToken)
            ?? BillingDefaults.CreatePlans().First(x => x.Code == "free");

        if (user is null)
        {
            return defaultPlan;
        }

        if (!string.IsNullOrWhiteSpace(user.SubscriptionPlanCode) &&
            user.SubscriptionPlanCode != "free" &&
            user.SubscriptionExpiresAtUtc.HasValue &&
            user.SubscriptionExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
        {
            user.SubscriptionPlanCode = "free";
            user.SubscriptionExpiresAtUtc = null;
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await appUserRepository.SaveChangesAsync(cancellationToken);
            return defaultPlan;
        }

        if (string.IsNullOrWhiteSpace(user.SubscriptionPlanCode))
        {
            user.SubscriptionPlanCode = "free";
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await appUserRepository.SaveChangesAsync(cancellationToken);
            return defaultPlan;
        }

        return await subscriptionPlanRepository.GetByCodeAsync(user.SubscriptionPlanCode, cancellationToken) ?? defaultPlan;
    }

    private async Task<int> GetManagedChannelRegistrationLimitAsync(AppUser user, CancellationToken cancellationToken)
    {
        var plans = await subscriptionPlanRepository.ListAsync(cancellationToken);
        var topPlanLimit = plans.Count == 0
            ? BillingDefaults.CreatePlans().Max(x => x.ManagedChannelLimit)
            : plans.Max(x => x.ManagedChannelLimit);

        return Math.Max(topPlanLimit + Math.Max(user.ExtraManagedChannelSlots, 0), 1);
    }

    private async Task<int> CountUsedUniqueChannelsAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var directSubscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var managedSubscriptions = await managedChannelSubscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);

        return directSubscriptions
            .Select(x => x.ChannelId)
            .Concat(managedSubscriptions.Select(x => x.ChannelId))
            .Distinct()
            .Count();
    }

    private async Task PauseOverflowManagedChannelsAsync(long telegramUserId, int activeLimit, CancellationToken cancellationToken)
    {
        var managedChannels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var activeChannels = managedChannels
            .Where(x => x.IsActive)
            .ToList();

        if (activeChannels.Count <= activeLimit)
        {
            return;
        }

        var overflowChannels = activeChannels
            .Skip(Math.Max(activeLimit, 0))
            .ToList();

        if (overflowChannels.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var overflowIds = overflowChannels
            .Select(x => x.Id)
            .ToHashSet();

        foreach (var channel in overflowChannels)
        {
            channel.IsActive = false;
            channel.UpdatedAtUtc = now;
        }

        var subscriptions = await managedChannelSubscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        foreach (var subscription in subscriptions.Where(x => overflowIds.Contains(x.ManagedChannelId) && x.IsActive))
        {
            subscription.IsActive = false;
            subscription.UpdatedAtUtc = now;
        }

        await managedChannelSubscriptionRepository.SaveChangesAsync(cancellationToken);
        await managedChannelRepository.SaveChangesAsync(cancellationToken);
    }

    private static string BuildPlanDescription(SubscriptionPlanDefinition plan)
    {
        return $"Monthly Telegram Stars subscription for up to {plan.ChannelLimit} source channels and {plan.ManagedChannelLimit} owned channels. Renews every 30 days.";
    }

    private static SubscriptionUsageDto MapUsage(
        SubscriptionPlanDefinition plan,
        int usedChannels,
        int usedManagedChannels,
        DateTimeOffset? expiresAtUtc,
        int extraSubscriptionSlots,
        int extraManagedChannelSlots)
    {
        var effectiveLimit = Math.Max(plan.ChannelLimit + Math.Max(extraSubscriptionSlots, 0), 1);
        var effectiveManagedChannelLimit = Math.Max(plan.ManagedChannelLimit + Math.Max(extraManagedChannelSlots, 0), 1);
        return new(
            plan.Code,
            plan.DisplayName,
            effectiveLimit,
            usedChannels,
            effectiveManagedChannelLimit,
            usedManagedChannels,
            expiresAtUtc,
            !plan.IsDefaultPlan);
    }

    private static SubscriptionPlanDefinitionDto MapPlan(SubscriptionPlanDefinition plan) =>
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

    private static DonationOptionDto MapDonation(DonationOption donation) =>
        new(
            donation.Id,
            donation.Code,
            donation.DisplayName,
            donation.StarsAmount,
            donation.IsEnabled,
            donation.SortOrder);

    private static int CountUsedManagedChannels(IReadOnlyList<ManagedChannel> managedChannels) =>
        managedChannels.Count(channel => channel.IsActive);
}
