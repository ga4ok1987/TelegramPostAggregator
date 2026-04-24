using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class PostRepository(AggregatorDbContext dbContext) : IPostRepository
{
    public Task<TelegramPost?> GetByChannelAndMessageIdAsync(Guid channelId, long telegramMessageId, CancellationToken cancellationToken = default) =>
        dbContext.TelegramPosts.FirstOrDefaultAsync(x => x.ChannelId == channelId && x.TelegramMessageId == telegramMessageId, cancellationToken);

    public async Task<IReadOnlyDictionary<long, TelegramPost>> GetByChannelAndMessageIdsAsync(
        Guid channelId,
        IReadOnlyCollection<long> telegramMessageIds,
        CancellationToken cancellationToken = default)
    {
        if (telegramMessageIds.Count == 0)
        {
            return new Dictionary<long, TelegramPost>();
        }

        return await dbContext.TelegramPosts
            .Where(x => x.ChannelId == channelId && telegramMessageIds.Contains(x.TelegramMessageId))
            .ToDictionaryAsync(x => x.TelegramMessageId, cancellationToken);
    }

    public Task<TelegramPost?> GetByIdAsync(Guid postId, CancellationToken cancellationToken = default) =>
        dbContext.TelegramPosts
            .Include(x => x.Channel)
            .FirstOrDefaultAsync(x => x.Id == postId, cancellationToken);

    public async Task<IReadOnlyList<TelegramPost>> GetFeedForUserAsync(long telegramUserId, int take, int skip, CancellationToken cancellationToken = default) =>
        await dbContext.TelegramPosts
            .Include(x => x.Channel)
            .Where(x => x.Channel.UserSubscriptions.Any(subscription => subscription.IsActive && subscription.User.TelegramUserId == telegramUserId))
            .OrderByDescending(x => x.PublishedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TelegramPost>> GetUndeliveredForChannelAsync(
        Guid channelId,
        long? lastDeliveredTelegramMessageId,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.TelegramPosts
            .Include(x => x.Channel)
            .Include(x => x.CollectorAccount)
            .Where(x => x.ChannelId == channelId && (!lastDeliveredTelegramMessageId.HasValue || x.TelegramMessageId > lastDeliveredTelegramMessageId.Value))
            .OrderBy(x => x.TelegramMessageId)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<long?> GetLatestTelegramMessageIdForChannelAsync(Guid channelId, CancellationToken cancellationToken = default) =>
        await dbContext.TelegramPosts
            .Where(x => x.ChannelId == channelId)
            .OrderByDescending(x => x.TelegramMessageId)
            .Select(x => (long?)x.TelegramMessageId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(TelegramPost post, CancellationToken cancellationToken = default) =>
        await dbContext.TelegramPosts.AddAsync(post, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
