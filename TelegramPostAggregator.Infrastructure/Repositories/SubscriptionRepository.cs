using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class SubscriptionRepository(AggregatorDbContext dbContext) : ISubscriptionRepository
{
    public Task<UserChannelSubscription?> GetAsync(Guid userId, Guid channelId, CancellationToken cancellationToken = default) =>
        dbContext.UserChannelSubscriptions.FirstOrDefaultAsync(x => x.UserId == userId && x.ChannelId == channelId, cancellationToken);

    public async Task<IReadOnlyList<UserChannelSubscription>> GetActiveByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions
            .Include(x => x.Channel)
            .Where(x => x.User.TelegramUserId == telegramUserId && x.IsActive)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserChannelSubscription>> GetActiveForDeliveryAsync(int take, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions
            .Include(x => x.User)
            .Include(x => x.Channel)
            .Where(x => x.IsActive && x.User.IsMonitoringEnabled && !x.User.IsBlockedBot)
            .OrderBy(x => x.LastDeliveredAtUtc ?? x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(UserChannelSubscription subscription, CancellationToken cancellationToken = default) =>
        await dbContext.UserChannelSubscriptions.AddAsync(subscription, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
