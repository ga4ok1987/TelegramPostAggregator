using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Services;

public sealed class AdminAuthService(
    IAdminUserRepository adminUserRepository,
    AdminPasswordHasher passwordHasher) : IAdminAuthService
{
    public async Task<AdminAuthenticatedUserDto?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var adminUser = await adminUserRepository.GetByNormalizedUsernameAsync(normalizedUsername, cancellationToken);
        if (adminUser is null || !adminUser.IsActive || !passwordHasher.Verify(adminUser.PasswordHash, password))
        {
            return null;
        }

        adminUser.LastLoginAtUtc = DateTimeOffset.UtcNow;
        adminUser.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await adminUserRepository.SaveChangesAsync(cancellationToken);

        return new AdminAuthenticatedUserDto(
            adminUser.Id,
            adminUser.Username,
            adminUser.DisplayName,
            adminUser.CanManageClients,
            adminUser.CanManageAdminUsers);
    }

    public async Task EnsureBootstrapAdminAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var existingUsers = await adminUserRepository.ListAsync(cancellationToken);
        if (existingUsers.Count > 0)
        {
            return;
        }

        var normalizedUsername = NormalizeUsername(username);
        var adminUser = new AdminUser
        {
            Username = username.Trim(),
            NormalizedUsername = normalizedUsername,
            DisplayName = username.Trim(),
            PasswordHash = passwordHasher.Hash(password),
            IsActive = true,
            CanManageClients = true,
            CanManageAdminUsers = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await adminUserRepository.AddAsync(adminUser, cancellationToken);
        await adminUserRepository.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeUsername(string username) =>
        username.Trim().ToUpperInvariant();
}
