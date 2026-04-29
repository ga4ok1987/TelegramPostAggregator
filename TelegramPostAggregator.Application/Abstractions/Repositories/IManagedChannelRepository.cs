using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IManagedChannelRepository
{
    Task<ManagedChannel?> GetAsync(Guid userId, Guid managedChannelId, CancellationToken cancellationToken = default);
    Task<ManagedChannel?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken cancellationToken = default);
    Task<ManagedChannel?> GetByTelegramChatIdAsync(Guid userId, long telegramChatId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedChannel>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task AddAsync(ManagedChannel managedChannel, CancellationToken cancellationToken = default);
    void Remove(ManagedChannel managedChannel);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
