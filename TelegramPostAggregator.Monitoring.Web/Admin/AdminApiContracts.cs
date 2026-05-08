namespace TelegramPostAggregator.Monitoring.Web.Admin;

public sealed record AdminClientUpdateRequest(bool IsBlockedBot);

public sealed record AdminClientSubscriptionAllowanceRequest(int ExtraSubscriptionSlots);

public sealed record AdminCreateSubscriptionRequest(string ChannelReference);

public sealed record AdminSetActiveRequest(bool IsActive);

public sealed record AdminUserCreateRequest(
    string Username,
    string DisplayName,
    string Password,
    bool IsActive,
    bool CanManageClients,
    bool CanManageAdminUsers);

public sealed record AdminUserUpdateRequest(
    string Username,
    string DisplayName,
    bool IsActive,
    bool CanManageClients,
    bool CanManageAdminUsers);

public sealed record AdminUserPasswordRequest(string Password);

public sealed record AdminPlanUpdateRequest(
    string DisplayName,
    int ChannelLimit,
    int PriceStars,
    int? DurationDays,
    bool IsEnabled,
    int SortOrder);

public sealed record AdminDonationUpdateRequest(
    string DisplayName,
    int StarsAmount,
    bool IsEnabled,
    int SortOrder);
