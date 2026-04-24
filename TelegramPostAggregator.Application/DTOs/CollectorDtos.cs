namespace TelegramPostAggregator.Application.DTOs;

public sealed record CollectorJoinResultDto(bool Success, string? ExternalChannelId, string? DisplayName, string? ErrorMessage);

public sealed record CollectedPostDto(
    long TelegramMessageId,
    DateTimeOffset PublishedAtUtc,
    string Text,
    string? MediaGroupId,
    bool HasMedia,
    bool IsForwarded,
    string? AuthorSignature,
    string? OriginalPostUrl,
    string MetadataJson);
