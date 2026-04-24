namespace TelegramPostAggregator.Application.DTOs;

public sealed record FeedItemDto(
    Guid PostId,
    string ChannelName,
    string ContentPreview,
    string NormalizedHash,
    DateTimeOffset PublishedAtUtc,
    bool HasMedia,
    bool IsForwarded,
    string? FactCheckStatus,
    Guid? DuplicateClusterId);
