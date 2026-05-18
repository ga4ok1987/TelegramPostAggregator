namespace TelegramPostAggregator.Application.DTOs;

public sealed record SubscriptionPlanDefinitionDto(
    Guid Id,
    string Code,
    string DisplayName,
    int ChannelLimit,
    int ManagedChannelLimit,
    int PriceStars,
    int? DurationDays,
    bool IsEnabled,
    bool IsDefaultPlan,
    int SortOrder);

public sealed record DonationOptionDto(
    Guid Id,
    string Code,
    string DisplayName,
    int StarsAmount,
    bool IsEnabled,
    int SortOrder);

public sealed record SubscriptionUsageDto(
    string CurrentPlanCode,
    string CurrentPlanName,
    int ChannelLimit,
    int UsedChannels,
    int ManagedChannelLimit,
    int UsedManagedChannels,
    DateTimeOffset? ExpiresAtUtc,
    bool IsPaidPlan);

public sealed record ChannelTrackingResultDto(
    bool Success,
    string Message,
    ChannelDto? Channel = null,
    SubscriptionUsageDto? Usage = null);

public sealed record BillingInvoiceRequestDto(
    long TelegramUserId,
    string TelegramUsername,
    string DisplayName,
    string ProductCode);

public sealed record BillingInvoiceResultDto(
    bool Success,
    string Message,
    TelegramBotInvoiceDto? Invoice = null);

public sealed record PreCheckoutDecisionDto(
    bool IsApproved,
    string? ErrorMessage = null);

public sealed record PaymentProcessingResultDto(
    bool Success,
    string Message);

public sealed record BillingSettingsDto(
    IReadOnlyList<SubscriptionPlanDefinitionDto> Plans,
    IReadOnlyList<DonationOptionDto> Donations,
    EmbeddingSettingsDto Embeddings);

public sealed record EmbeddingSettingsDto(
    bool IsEnabled,
    string Model,
    int RetentionDays,
    IReadOnlyList<EmbeddingApiKeyDto> ApiKeys,
    EmbeddingStatusOverviewDto Status);

public sealed record EmbeddingApiKeyDto(
    Guid Id,
    string DisplayName,
    string MaskedKey,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public sealed record EmbeddingStatusOverviewDto(
    int ReadyCount,
    int PendingCount,
    int FailedCount,
    int StoredVectorCount);
