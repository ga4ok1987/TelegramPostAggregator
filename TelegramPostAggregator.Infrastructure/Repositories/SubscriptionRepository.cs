using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class SubscriptionRepository(AggregatorDbContext dbContext) : ISubscriptionRepository
{
    public Task<UserChannelSubscription?> GetAsync(Guid userId, Guid channelId, CancellationToken cancellationToken = default) =>
        dbContext.UserChannelSubscriptions.FirstOrDefaultAsync(x => x.UserId == userId && x.ChannelId == channelId, cancellationToken);

    public async Task<IReadOnlyList<UserChannelSubscription>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions
            .Include(x => x.Channel)
            .Include(x => x.User)
            .Where(x => x.User.TelegramUserId == telegramUserId)
            .OrderBy(x => x.Channel.ChannelName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserChannelSubscription>> GetActiveByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions
            .Include(x => x.Channel)
            .Where(x => x.User.TelegramUserId == telegramUserId && x.IsActive)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserChannelSubscription>> GetActiveForDeliveryAsync(int take, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions
            .Include(x => x.User)
            .Include(x => x.Channel)
            .Where(x => x.IsActive)
            .OrderBy(x => x.LastDeliveredAtUtc ?? x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(UserChannelSubscription subscription, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions.AddAsync(subscription, cancellationToken);

    public void Remove(UserChannelSubscription subscription) =>
        dbContext.UserChannelSubscriptions.Remove(subscription);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
