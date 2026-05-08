using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class DonationOption : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int StarsAmount { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
}
