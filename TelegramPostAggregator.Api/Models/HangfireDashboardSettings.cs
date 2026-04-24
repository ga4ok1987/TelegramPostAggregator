namespace TelegramPostAggregator.Api.Models;

public sealed class HangfireDashboardSettings
{
    public const string SectionName = "Operations:HangfireDashboard";

    public bool Enabled { get; set; }

    public bool AllowLocalRequests { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }
}
