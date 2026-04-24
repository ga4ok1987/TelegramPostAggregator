namespace TelegramPostAggregator.Application.Options;

public sealed class FactCheckOptions
{
    public const string SectionName = "FactCheck";

    public int BatchSize { get; set; } = 20;
    public string DefaultProvider { get; set; } = "MockAiVerifier";
}
