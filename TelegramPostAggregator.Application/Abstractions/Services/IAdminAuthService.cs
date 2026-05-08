using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IAdminAuthService
{
    Task<AdminAuthenticatedUserDto?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

    Task EnsureBootstrapAdminAsync(string username, string password, CancellationToken cancellationToken = default);
}
