namespace TelegramPostAggregator.Application.Options;

public sealed class MiniAppOptions
{
    public const string SectionName = "MiniApp";

    public string Url { get; set; } = string.Empty;
    public int InitDataLifetimeSeconds { get; set; } = 900;
}
