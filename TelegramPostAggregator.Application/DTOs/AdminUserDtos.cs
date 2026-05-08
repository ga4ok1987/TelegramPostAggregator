namespace TelegramPostAggregator.Application.DTOs;

public sealed record AdminAuthenticatedUserDto(
    Guid AdminUserId,
    string Username,
    string DisplayName,
    bool CanManageClients,
    bool CanManageAdminUsers);

public sealed record AdminUserDto(
    Guid AdminUserId,
    string Username,
    string DisplayName,
    bool IsActive,
    bool CanManageClients,
    bool CanManageAdminUsers,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    bool IsCurrentUser);

public sealed record AdminUserDetailDto(
    Guid AdminUserId,
    string Username,
    string DisplayName,
    bool IsActive,
    bool CanManageClients,
    bool CanManageAdminUsers,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    bool IsCurrentUser);

public sealed record AdminUserCreateDto(
    string Username,
    string DisplayName,
    string Password,
    bool IsActive,
    bool CanManageClients,
    bool CanManageAdminUsers);

public sealed record AdminUserUpdateDto(
    string Username,
    string DisplayName,
    bool IsActive,
    bool CanManageClients,
    bool CanManageAdminUsers);

public sealed record AdminCommandResultDto(bool Success, string? ErrorMessage = null);

public sealed record AdminUserCommandResultDto(bool Success, string? ErrorMessage = null, AdminUserDetailDto? User = null);
