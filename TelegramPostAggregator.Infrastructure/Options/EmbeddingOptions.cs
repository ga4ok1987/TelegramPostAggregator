namespace TelegramPostAggregator.Infrastructure.Options;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 20;
    public int CleanupBatchSize { get; set; } = 200;
}
