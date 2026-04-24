using TelegramPostAggregator.Domain.Common;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class TelegramPost : BaseEntity
{
    public Guid ChannelId { get; set; }
    public TrackedChannel Channel { get; set; } = null!;

    public Guid CollectorAccountId { get; set; }
    public CollectorAccount CollectorAccount { get; set; } = null!;

    public Guid? DuplicateClusterId { get; set; }
    public PostDuplicateCluster? DuplicateCluster { get; set; }

    public long TelegramMessageId { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
    public string? AuthorSignature { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string? MediaGroupId { get; set; }
    public bool HasMedia { get; set; }
    public bool IsForwarded { get; set; }
    public string? OriginalPostUrl { get; set; }
    public PostSourceKind SourceKind { get; set; } = PostSourceKind.ChannelPost;
    public string MetadataJson { get; set; } = "{}";
}
