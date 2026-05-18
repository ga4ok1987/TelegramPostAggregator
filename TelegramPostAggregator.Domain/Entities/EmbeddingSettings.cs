using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class EmbeddingSettings : BaseEntity
{
    public bool IsEnabled { get; set; } = true;
    public string Model { get; set; } = "text-embedding-3-small";
    public int RetentionDays { get; set; } = 7;
}
