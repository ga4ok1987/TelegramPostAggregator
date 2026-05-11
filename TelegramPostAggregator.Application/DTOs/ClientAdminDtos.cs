namespace TelegramPostAggregator.Application.DTOs;

public sealed record AdminPagedResultDto<T>(
    IReadOnlyCollection<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record AdminBotSubscriptionDto(
    Guid SubscriptionId,
    Guid ChannelId,
    string ChannelName,
    string ChannelReference,
    bool IsActive,
    DateTimeOffset? LastDeliveredAtUtc);

public sealed record AdminManagedChannelSourceSubscriptionDto(
    Guid SubscriptionId,
    Guid ChannelId,
    string ChannelName,
    string ChannelReference,
    bool IsActive,
    DateTimeOffset? LastDeliveredAtUtc);

public sealed record AdminClientChannelDto(
    Guid ManagedChannelId,
    string ChannelName,
    string ChannelReference,
    bool IsActive,
    int SubscriptionCount,
    int ActiveSubscriptionCount,
    DateTimeOffset? LastWriteSucceededAtUtc,
    string? LastWriteError);

public sealed record AdminClientDto(
    Guid UserId,
    long TelegramUserId,
    string TelegramUsername,
    string DisplayName,
    string PreferredLanguageCode,
    bool IsBlockedBot,
    DateTimeOffset CreatedAtUtc,
    int ManagedChannelsCount,
    int ActiveManagedChannelsCount,
    int TotalSubscriptionsCount,
    int ActiveSubscriptionsCount);

public sealed record AdminClientDetailDto(
    Guid UserId,
    long TelegramUserId,
    string TelegramUsername,
    string DisplayName,
    string PreferredLanguageCode,
    bool IsBlockedBot,
    DateTimeOffset CreatedAtUtc,
    string CurrentPlanName,
    int SubscriptionLimit,
    int UsedSubscriptionsCount,
    int ExtraSubscriptionSlots,
    int ManagedChannelLimit,
    int UsedManagedChannelsCount,
    int ExtraManagedChannelSlots,
    DateTimeOffset? SubscriptionExpiresAtUtc,
    int ManagedChannelsCount,
    int ActiveManagedChannelsCount,
    int TotalManagedChannelSubscriptionsCount,
    int ActiveManagedChannelSubscriptionsCount,
    int BotSubscriptionsCount,
    int ActiveBotSubscriptionsCount,
    IReadOnlyCollection<AdminClientChannelDto> ManagedChannels);
