namespace TelegramPostAggregator.Application.Options;

public sealed class BotMonitoringOptions
{
    public const string SectionName = "BotMonitoring";

    public string AllowedEmail { get; set; } = string.Empty;

    public int RefreshIntervalSeconds { get; set; } = 30;

    public List<BotDefinitionOptions> Bots { get; set; } = [];
}

public sealed class BotDefinitionOptions
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ProbeType { get; set; } = "http";

    public string? Endpoint { get; set; }

    public string? HeartbeatFilePath { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    public int StaleAfterSeconds { get; set; } = 180;

    public List<string> Tags { get; set; } = [];
}
