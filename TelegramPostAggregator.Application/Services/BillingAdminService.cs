using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Application.Services;

public sealed class BillingAdminService(
    ISubscriptionPlanRepository subscriptionPlanRepository,
    IDonationOptionRepository donationOptionRepository,
    IEmbeddingSettingsRepository embeddingSettingsRepository,
    IOpenAiApiKeyRepository openAiApiKeyRepository,
    IPostRepository postRepository,
    ITelegramPostEmbeddingRepository telegramPostEmbeddingRepository) : IBillingAdminService
{
    public async Task<BillingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var plans = await subscriptionPlanRepository.ListAsync(cancellationToken);
        var donations = await donationOptionRepository.ListAsync(cancellationToken);
        var embeddingSettings = await GetOrCreateEmbeddingSettingsAsync(cancellationToken);
        var apiKeys = await openAiApiKeyRepository.ListAsync(cancellationToken);
        var status = await GetEmbeddingStatusOverviewAsync(cancellationToken);

        return new BillingSettingsDto(
            plans.OrderBy(x => x.SortOrder).Select(MapPlan).ToArray(),
            donations.OrderBy(x => x.SortOrder).Select(MapDonation).ToArray(),
            MapEmbeddingSettings(embeddingSettings, apiKeys, status));
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

    public async Task<EmbeddingSettingsDto> UpdateEmbeddingSettingsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateEmbeddingSettingsAsync(cancellationToken);
        settings.RetentionDays = Math.Clamp(retentionDays, 1, 30);
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await embeddingSettingsRepository.SaveChangesAsync(cancellationToken);
        var apiKeys = await openAiApiKeyRepository.ListAsync(cancellationToken);
        var status = await GetEmbeddingStatusOverviewAsync(cancellationToken);
        return MapEmbeddingSettings(settings, apiKeys, status);
    }

    public async Task<EmbeddingApiKeyDto> AddEmbeddingApiKeyAsync(string displayName, string apiKey, CancellationToken cancellationToken = default)
    {
        var trimmedName = string.IsNullOrWhiteSpace(displayName)
            ? $"OpenAI key {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}"
            : displayName.Trim();

        var trimmedKey = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(trimmedKey))
        {
            throw new InvalidOperationException("API key is required.");
        }

        var existingKeys = await openAiApiKeyRepository.ListAsync(cancellationToken);
        foreach (var existing in existingKeys)
        {
            existing.IsActive = false;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        var key = new Domain.Entities.OpenAiApiKey
        {
            DisplayName = trimmedName,
            ApiKey = trimmedKey,
            IsActive = true,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await openAiApiKeyRepository.AddAsync(key, cancellationToken);
        await openAiApiKeyRepository.SaveChangesAsync(cancellationToken);
        return MapApiKey(key);
    }

    public async Task<bool> DeleteEmbeddingApiKeyAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        var key = await openAiApiKeyRepository.GetByIdAsync(keyId, cancellationToken);
        if (key is null)
        {
            return false;
        }

        var wasActive = key.IsActive;
        openAiApiKeyRepository.Remove(key);
        await openAiApiKeyRepository.SaveChangesAsync(cancellationToken);

        if (wasActive)
        {
            var remainingKeys = await openAiApiKeyRepository.ListAsync(cancellationToken);
            var replacement = remainingKeys
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();

            if (replacement is not null)
            {
                replacement.IsActive = true;
                replacement.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await openAiApiKeyRepository.SaveChangesAsync(cancellationToken);
            }
        }

        return true;
    }

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        await BillingDefaultsSeeder.EnsureAsync(
            subscriptionPlanRepository,
            donationOptionRepository,
            cancellationToken);
    }

    private async Task<Domain.Entities.EmbeddingSettings> GetOrCreateEmbeddingSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await embeddingSettingsRepository.GetAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new Domain.Entities.EmbeddingSettings();
        await embeddingSettingsRepository.AddAsync(settings, cancellationToken);
        await embeddingSettingsRepository.SaveChangesAsync(cancellationToken);
        return settings;
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

    private async Task<EmbeddingStatusOverviewDto> GetEmbeddingStatusOverviewAsync(CancellationToken cancellationToken)
    {
        var ready = await postRepository.CountByEmbeddingStatusAsync(EmbeddingStatus.Ready, cancellationToken);
        var pending = await postRepository.CountByEmbeddingStatusAsync(EmbeddingStatus.Pending, cancellationToken)
            + await postRepository.CountByEmbeddingStatusAsync(EmbeddingStatus.PendingRefresh, cancellationToken)
            + await postRepository.CountByEmbeddingStatusAsync(EmbeddingStatus.Processing, cancellationToken);
        var failed = await postRepository.CountByEmbeddingStatusAsync(EmbeddingStatus.Failed, cancellationToken);
        var storedVectors = await telegramPostEmbeddingRepository.CountAsync(cancellationToken);

        return new EmbeddingStatusOverviewDto(ready, pending, failed, storedVectors);
    }

    private static EmbeddingSettingsDto MapEmbeddingSettings(
        Domain.Entities.EmbeddingSettings settings,
        IReadOnlyList<Domain.Entities.OpenAiApiKey> apiKeys,
        EmbeddingStatusOverviewDto status) =>
        new(
            settings.IsEnabled,
            settings.Model,
            settings.RetentionDays,
            apiKeys
                .OrderByDescending(x => x.IsActive)
                .ThenByDescending(x => x.CreatedAtUtc)
                .Select(MapApiKey)
                .ToArray(),
            status);

    private static EmbeddingApiKeyDto MapApiKey(Domain.Entities.OpenAiApiKey key) =>
        new(
            key.Id,
            key.DisplayName,
            MaskKey(key.ApiKey),
            key.IsActive,
            key.CreatedAtUtc);

    private static string MaskKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 8)
        {
            return "****";
        }

        return $"{trimmed[..6]}...{trimmed[^4..]}";
    }
}
