using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class SubscriptionPlanDefinition : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int ChannelLimit { get; set; }
    public int PriceStars { get; set; }
    public int? DurationDays { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsDefaultPlan { get; set; }
    public int SortOrder { get; set; }
}
