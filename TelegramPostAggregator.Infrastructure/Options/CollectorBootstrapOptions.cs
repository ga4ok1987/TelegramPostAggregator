namespace TelegramPostAggregator.Infrastructure.Options;

public sealed class CollectorBootstrapOptions
{
    public const string SectionName = "CollectorBootstrap";

    public string Name { get; set; } = "primary-collector";
    public string ExternalAccountKey { get; set; } = "collector-1";
    public string PhoneNumber { get; set; } = string.Empty;
}
