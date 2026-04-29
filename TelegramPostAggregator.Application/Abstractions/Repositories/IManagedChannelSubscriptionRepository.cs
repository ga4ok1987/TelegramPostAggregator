using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IManagedChannelSubscriptionRepository
{
    Task<ManagedChannelSubscription?> GetAsync(Guid managedChannelId, Guid channelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedChannelSubscription>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedChannelSubscription>> GetByManagedChannelIdAsync(Guid managedChannelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedChannelSubscription>> GetActiveForDeliveryAsync(int take, CancellationToken cancellationToken = default);
    Task AddAsync(ManagedChannelSubscription subscription, CancellationToken cancellationToken = default);
    void Remove(ManagedChannelSubscription subscription);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
