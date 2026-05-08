using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class SubscriptionRepository(AggregatorDbContext dbContext) : ISubscriptionRepository
{
    public Task<UserChannelSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default) =>
        dbContext.UserChannelSubscriptions
            .Include(x => x.User)
            .Include(x => x.Channel)
            .FirstOrDefaultAsync(x => x.Id == subscriptionId, cancellationToken);

    public Task<UserChannelSubscription?> GetAsync(Guid userId, Guid channelId, CancellationToken cancellationToken = default) =>
        dbContext.UserChannelSubscriptions.FirstOrDefaultAsync(x => x.UserId == userId && x.ChannelId == channelId, cancellationToken);

    public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.UserChannelSubscriptions.CountAsync(x => x.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<UserChannelSubscription>> GetPageByUserIdAsync(Guid userId, int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions
            .AsNoTracking()
            .Include(x => x.Channel)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Channel.ChannelName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserChannelSubscription>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions
            .Include(x => x.Channel)
            .Include(x => x.User)
            .Where(x => x.User.TelegramUserId == telegramUserId)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Channel.ChannelName)
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
