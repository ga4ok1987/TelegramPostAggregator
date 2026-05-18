namespace TelegramPostAggregator.Infrastructure.Options;

public sealed class TdLibOptions
{
    public const string SectionName = "TdLib";

    public string DatabaseDirectory { get; set; } = "./tdlib-data";
    public string FilesDirectory { get; set; } = "./tdlib-files";
    public bool UseSimulation { get; set; } = true;
    public int ApiId { get; set; }
    public string ApiHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = "TelegramPostAggregator";
    public string SystemVersion { get; set; } = "Server";
    public string SystemLanguageCode { get; set; } = "en";
    public string ApplicationVersion { get; set; } = "1.0.0";
    public int AuthorizationTimeoutSeconds { get; set; } = 30;
    public bool MediaCacheCleanupEnabled { get; set; } = true;
    public int MediaCacheRetentionHours { get; set; } = 24;
    public int MediaCacheCleanupBatchSize { get; set; } = 500;
    public int DiskAlertThresholdPercent { get; set; } = 80;
    public int DiskEmergencyCleanupThresholdPercent { get; set; } = 85;
    public int DiskRecoveryTargetPercent { get; set; } = 80;
    public int EmergencyMediaCacheCleanupBatchSize { get; set; } = 5000;
    public int EmergencyMediaCacheMinimumAgeHours { get; set; } = 6;
}
