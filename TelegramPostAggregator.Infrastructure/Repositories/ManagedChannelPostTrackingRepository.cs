using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class ManagedChannelPostTrackingRepository(AggregatorDbContext dbContext) : IManagedChannelPostTrackingRepository
{
    public Task<ManagedChannelPostTracking?> GetAsync(Guid managedChannelSubscriptionId, Guid postId, CancellationToken cancellationToken = default) =>
        dbContext.ManagedChannelPostTrackings.FirstOrDefaultAsync(
            x => x.ManagedChannelSubscriptionId == managedChannelSubscriptionId && x.PostId == postId,
            cancellationToken);

    public async Task<IReadOnlyList<ManagedChannelPostTracking>> GetBySubscriptionAndPostIdsAsync(
        Guid managedChannelSubscriptionId,
        IReadOnlyCollection<Guid> postIds,
        CancellationToken cancellationToken = default)
    {
        if (postIds.Count == 0)
        {
            return [];
        }

        return await dbContext.ManagedChannelPostTrackings
            .Where(x => x.ManagedChannelSubscriptionId == managedChannelSubscriptionId && postIds.Contains(x.PostId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ManagedChannelPostTracking>> GetByPostIdAsync(Guid postId, CancellationToken cancellationToken = default) =>
        await dbContext.ManagedChannelPostTrackings
            .Include(x => x.ManagedChannel)
            .Include(x => x.ManagedChannelSubscription)
            .Where(x => x.PostId == postId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ManagedChannelPostTracking>> GetPendingAsync(int take, CancellationToken cancellationToken = default) =>
        await dbContext.ManagedChannelPostTrackings
            .Include(x => x.ManagedChannel)
            .Include(x => x.ManagedChannelSubscription)
            .ThenInclude(x => x.Channel)
            .Include(x => x.Post)
            .ThenInclude(x => x.Channel)
            .Include(x => x.Post)
            .ThenInclude(x => x.CollectorAccount)
            .Where(x => x.PendingEditedAtUtc.HasValue)
            .OrderBy(x => x.PendingEditedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task AddAsync(ManagedChannelPostTracking tracking, CancellationToken cancellationToken = default) =>
        dbContext.ManagedChannelPostTrackings.AddAsync(tracking, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
