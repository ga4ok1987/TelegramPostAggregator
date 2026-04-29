using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class ManagedChannelSubscriptionRepository(AggregatorDbContext dbContext) : IManagedChannelSubscriptionRepository
{
    public Task<ManagedChannelSubscription?> GetAsync(Guid managedChannelId, Guid channelId, CancellationToken cancellationToken = default) =>
        dbContext.ManagedChannelSubscriptions.FirstOrDefaultAsync(
            x => x.ManagedChannelId == managedChannelId && x.ChannelId == channelId,
            cancellationToken);

    public async Task<IReadOnlyList<ManagedChannelSubscription>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        await dbContext.ManagedChannelSubscriptions
            .Include(x => x.ManagedChannel)
            .Include(x => x.Channel)
            .Where(x => x.ManagedChannel.User.TelegramUserId == telegramUserId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ManagedChannelSubscription>> GetByManagedChannelIdAsync(Guid managedChannelId, CancellationToken cancellationToken = default) =>
        await dbContext.ManagedChannelSubscriptions
            .Include(x => x.ManagedChannel)
            .Include(x => x.Channel)
            .Where(x => x.ManagedChannelId == managedChannelId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ManagedChannelSubscription>> GetActiveForDeliveryAsync(int take, CancellationToken cancellationToken = default) =>
        await dbContext.ManagedChannelSubscriptions
            .Include(x => x.ManagedChannel)
            .Include(x => x.Channel)
            .Where(x => x.IsActive && x.ManagedChannel.IsActive)
            .OrderBy(x => x.LastDeliveredAtUtc ?? x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task AddAsync(ManagedChannelSubscription subscription, CancellationToken cancellationToken = default) =>
        dbContext.ManagedChannelSubscriptions.AddAsync(subscription, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
