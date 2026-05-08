using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IAdminUserRepository
{
    Task<AdminUser?> GetByIdAsync(Guid adminUserId, CancellationToken cancellationToken = default);

    Task<AdminUser?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(AdminUser adminUser, CancellationToken cancellationToken = default);

    void Remove(AdminUser adminUser);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
