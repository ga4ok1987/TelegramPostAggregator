using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class PostDuplicateCluster : BaseEntity
{
    public Guid? CanonicalPostId { get; set; }
    public TelegramPost? CanonicalPost { get; set; }
    public string ClusterKey { get; set; } = string.Empty;
    public string SummaryNormalizedText { get; set; } = string.Empty;

    public ICollection<TelegramPost> Posts { get; set; } = new List<TelegramPost>();
}
