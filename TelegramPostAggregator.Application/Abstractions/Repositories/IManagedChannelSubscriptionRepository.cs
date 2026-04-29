using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IManagedChannelSubscriptionRepository
{
    Task<ManagedChannelSubscription?> GetAsync(Guid managedChannelId, Guid channelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedChannelSubscription>> GetActiveForDeliveryAsync(int take, CancellationToken cancellationToken = default);
    Task AddAsync(ManagedChannelSubscription subscription, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
