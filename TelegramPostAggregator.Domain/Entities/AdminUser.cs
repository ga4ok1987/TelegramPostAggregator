using TelegramPostAggregator.Domain.Common;

namespace TelegramPostAggregator.Domain.Entities;

public sealed class AdminUser : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool CanManageClients { get; set; } = true;
    public bool CanManageAdminUsers { get; set; } = true;
    public DateTimeOffset? LastLoginAtUtc { get; set; }
}
