using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class TelegramPostRevision : BaseEntity
{
    public Guid PostId { get; set; }
    public TelegramPost Post { get; set; } = null!;

    public int RevisionNumber { get; set; }
    public bool IsEdited { get; set; }
    public DateTimeOffset? TelegramEditDateUtc { get; set; }

    public string RawText { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public string? MediaGroupId { get; set; }
    public bool HasMedia { get; set; }
    public string? OriginalPostUrl { get; set; }
    public string MetadataJson { get; set; } = "{}";
}
