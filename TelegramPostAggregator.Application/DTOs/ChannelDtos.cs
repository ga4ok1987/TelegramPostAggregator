namespace TelegramPostAggregator.Application.DTOs;

public sealed record AddTrackedChannelDto(long TelegramUserId, string TelegramUsername, string DisplayName, string ChannelReference);

public sealed record RemoveTrackedChannelDto(long TelegramUserId, string ChannelReference);

public sealed record ChannelDto(
    Guid Id,
    string ChannelName,
    string ChannelReference,
    string Status,
    DateTimeOffset? LastPostCollectedAtUtc,
    string? LastCollectorError);
