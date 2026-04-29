namespace TelegramPostAggregator.Application.DTOs;

public sealed record MiniAppChannelDto(
    Guid ChannelId,
    string ChannelName,
    string ChannelReference,
    string Status,
    bool IsActive,
    DateTimeOffset? LastPostCollectedAtUtc,
    string? LastCollectorError,
    IReadOnlyList<MiniAppSourceSubscriptionDto> Subscriptions);

public sealed record MiniAppSourceSubscriptionDto(
    Guid SubscriptionId,
    Guid SourceChannelId,
    string ChannelName,
    string ChannelReference,
    string Status,
    bool IsActive,
    DateTimeOffset? LastDeliveredAtUtc,
    string? LastCollectorError);

public sealed record ManagedChannelRegistrationResultDto(
    bool Success,
    string Message);

public sealed record MiniAppAuthResultDto(
    bool IsAuthenticated,
    long? TelegramUserId,
    string? Username,
    string? FirstName,
    string? LastName,
    string? ErrorMessage);
