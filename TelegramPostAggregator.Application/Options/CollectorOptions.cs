namespace TelegramPostAggregator.Application.Options;

public sealed class CollectorOptions
{
    public const string SectionName = "Collector";

    public int SubscriptionBatchSize { get; set; } = 20;
    public int PostSyncBatchSize { get; set; } = 100;
    public bool UseTdLibSimulation { get; set; } = true;
}
