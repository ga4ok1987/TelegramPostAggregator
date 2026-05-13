using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IManagedChannelPostTrackingRepository
{
    Task<ManagedChannelPostTracking?> GetAsync(Guid managedChannelSubscriptionId, Guid postId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedChannelPostTracking>> GetBySubscriptionAndPostIdsAsync(Guid managedChannelSubscriptionId, IReadOnlyCollection<Guid> postIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedChannelPostTracking>> GetByPostIdAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedChannelPostTracking>> GetPendingAsync(int take, CancellationToken cancellationToken = default);
    Task AddAsync(ManagedChannelPostTracking tracking, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
