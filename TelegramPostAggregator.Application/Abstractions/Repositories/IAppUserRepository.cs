using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AppUser?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppUser>> ListForAdminAsync(CancellationToken cancellationToken = default);
    Task AddAsync(AppUser user, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
