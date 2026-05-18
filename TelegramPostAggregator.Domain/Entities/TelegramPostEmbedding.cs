using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class TelegramPostEmbedding : BaseEntity
{
    public Guid PostId { get; set; }
    public TelegramPost Post { get; set; } = null!;

    public string Model { get; set; } = "text-embedding-3-small";
    public int TextVersion { get; set; } = 1;
    public string NormalizedText { get; set; } = string.Empty;
    public string VectorJson { get; set; } = "[]";
    public int Dimensions { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
