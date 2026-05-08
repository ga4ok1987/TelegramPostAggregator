using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Services;

public sealed class AdminUserService(
    IAdminUserRepository adminUserRepository,
    AdminPasswordHasher passwordHasher) : IAdminUserService
{
    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(Guid currentAdminUserId, CancellationToken cancellationToken = default)
    {
        var users = await adminUserRepository.ListAsync(cancellationToken);
        return users
            .Select(user => MapSummary(user, currentAdminUserId))
            .ToArray();
    }

    public async Task<AdminUserDetailDto?> GetAsync(Guid adminUserId, Guid currentAdminUserId, CancellationToken cancellationToken = default)
    {
        var user = await adminUserRepository.GetByIdAsync(adminUserId, cancellationToken);
        return user is null ? null : MapDetail(user, currentAdminUserId);
    }

    public async Task<AdminUserCommandResultDto> CreateAsync(AdminUserCreateDto request, Guid currentAdminUserId, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateCommon(request.Username, request.DisplayName);
        if (validationError is not null)
        {
            return new AdminUserCommandResultDto(false, validationError);
        }

        var passwordError = ValidatePassword(request.Password);
        if (passwordError is not null)
        {
            return new AdminUserCommandResultDto(false, passwordError);
        }

        var normalizedUsername = NormalizeUsername(request.Username);
        if (await adminUserRepository.GetByNormalizedUsernameAsync(normalizedUsername, cancellationToken) is not null)
        {
            return new AdminUserCommandResultDto(false, "Username already exists.");
        }

        var adminUser = new AdminUser
        {
            Username = request.Username.Trim(),
            NormalizedUsername = normalizedUsername,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = request.IsActive,
            CanManageClients = request.CanManageClients,
            CanManageAdminUsers = request.CanManageAdminUsers,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await adminUserRepository.AddAsync(adminUser, cancellationToken);
        await adminUserRepository.SaveChangesAsync(cancellationToken);

        return new AdminUserCommandResultDto(true, User: MapDetail(adminUser, currentAdminUserId));
    }

    public async Task<AdminUserCommandResultDto> UpdateAsync(Guid adminUserId, AdminUserUpdateDto request, Guid currentAdminUserId, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateCommon(request.Username, request.DisplayName);
        if (validationError is not null)
        {
            return new AdminUserCommandResultDto(false, validationError);
        }

        var adminUser = await adminUserRepository.GetByIdAsync(adminUserId, cancellationToken);
        if (adminUser is null)
        {
            return new AdminUserCommandResultDto(false, "Admin user not found.");
        }

        var normalizedUsername = NormalizeUsername(request.Username);
        var existingWithUsername = await adminUserRepository.GetByNormalizedUsernameAsync(normalizedUsername, cancellationToken);
        if (existingWithUsername is not null && existingWithUsername.Id != adminUserId)
        {
            return new AdminUserCommandResultDto(false, "Username already exists.");
        }

        var remainingAdminManagers = await CountRemainingAdminManagersAsync(adminUserId, request.IsActive, request.CanManageAdminUsers, cancellationToken);
        if (remainingAdminManagers == 0)
        {
            return new AdminUserCommandResultDto(false, "At least one active user with admin-management rights must remain.");
        }

        adminUser.Username = request.Username.Trim();
        adminUser.NormalizedUsername = normalizedUsername;
        adminUser.DisplayName = request.DisplayName.Trim();
        adminUser.IsActive = request.IsActive;
        adminUser.CanManageClients = request.CanManageClients;
        adminUser.CanManageAdminUsers = request.CanManageAdminUsers;
        adminUser.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await adminUserRepository.SaveChangesAsync(cancellationToken);
        return new AdminUserCommandResultDto(true, User: MapDetail(adminUser, currentAdminUserId));
    }

    public async Task<AdminCommandResultDto> SetPasswordAsync(Guid adminUserId, string newPassword, Guid currentAdminUserId, CancellationToken cancellationToken = default)
    {
        var passwordError = ValidatePassword(newPassword);
        if (passwordError is not null)
        {
            return new AdminCommandResultDto(false, passwordError);
        }

        var adminUser = await adminUserRepository.GetByIdAsync(adminUserId, cancellationToken);
        if (adminUser is null)
        {
            return new AdminCommandResultDto(false, "Admin user not found.");
        }

        adminUser.PasswordHash = passwordHasher.Hash(newPassword);
        adminUser.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await adminUserRepository.SaveChangesAsync(cancellationToken);

        return new AdminCommandResultDto(true);
    }

    public async Task<AdminCommandResultDto> DeleteAsync(Guid adminUserId, Guid currentAdminUserId, CancellationToken cancellationToken = default)
    {
        if (adminUserId == currentAdminUserId)
        {
            return new AdminCommandResultDto(false, "You cannot delete the account you are currently signed in with.");
        }

        var adminUser = await adminUserRepository.GetByIdAsync(adminUserId, cancellationToken);
        if (adminUser is null)
        {
            return new AdminCommandResultDto(false, "Admin user not found.");
        }

        var remainingAdminManagers = await CountRemainingAdminManagersAsync(adminUserId, false, false, cancellationToken);
        if (remainingAdminManagers == 0)
        {
            return new AdminCommandResultDto(false, "At least one active user with admin-management rights must remain.");
        }

        adminUserRepository.Remove(adminUser);
        await adminUserRepository.SaveChangesAsync(cancellationToken);
        return new AdminCommandResultDto(true);
    }

    private async Task<int> CountRemainingAdminManagersAsync(Guid targetUserId, bool targetIsActive, bool targetCanManageAdminUsers, CancellationToken cancellationToken)
    {
        var users = await adminUserRepository.ListAsync(cancellationToken);
        return users.Count(user =>
        {
            if (user.Id == targetUserId)
            {
                return targetIsActive && targetCanManageAdminUsers;
            }

            return user.IsActive && user.CanManageAdminUsers;
        });
    }

    private static AdminUserDto MapSummary(AdminUser user, Guid currentAdminUserId) =>
        new(
            user.Id,
            user.Username,
            user.DisplayName,
            user.IsActive,
            user.CanManageClients,
            user.CanManageAdminUsers,
            user.CreatedAtUtc,
            user.LastLoginAtUtc,
            user.Id == currentAdminUserId);

    private static AdminUserDetailDto MapDetail(AdminUser user, Guid currentAdminUserId) =>
        new(
            user.Id,
            user.Username,
            user.DisplayName,
            user.IsActive,
            user.CanManageClients,
            user.CanManageAdminUsers,
            user.CreatedAtUtc,
            user.LastLoginAtUtc,
            user.Id == currentAdminUserId);

    private static string? ValidateCommon(string username, string displayName)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "Username is required.";
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Display name is required.";
        }

        if (username.Trim().Length < 3)
        {
            return "Username must be at least 3 characters.";
        }

        if (displayName.Trim().Length < 2)
        {
            return "Display name must be at least 2 characters.";
        }

        return null;
    }

    private static string? ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "Password is required.";
        }

        if (password.Trim().Length < 8)
        {
            return "Password must be at least 8 characters.";
        }

        return null;
    }

    private static string NormalizeUsername(string username) =>
        username.Trim().ToUpperInvariant();
}
