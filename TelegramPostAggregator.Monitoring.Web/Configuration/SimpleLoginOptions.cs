namespace TelegramPostAggregator.Monitoring.Web.Configuration;

public sealed class SimpleLoginOptions
{
    public const string SectionName = "SimpleLogin";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
