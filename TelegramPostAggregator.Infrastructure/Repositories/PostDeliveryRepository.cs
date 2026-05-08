using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class PostDeliveryRepository(AggregatorDbContext dbContext) : IPostDeliveryRepository
{
    public Task<TelegramPostDelivery?> GetLatestForPostAndDestinationAsync(
        Guid postId,
        PostDeliveryDestinationKind destinationKind,
        long destinationChatId,
        CancellationToken cancellationToken = default) =>
        dbContext.TelegramPostDeliveries
            .Where(x => x.PostId == postId && x.DestinationKind == destinationKind && x.DestinationChatId == destinationChatId)
            .OrderByDescending(x => x.RevisionNumber)
            .ThenByDescending(x => x.DeliveredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TelegramPost>> GetPendingEditedPostsForDestinationAsync(
        Guid channelId,
        PostDeliveryDestinationKind destinationKind,
        long destinationChatId,
        long deliveredThroughTelegramMessageId,
        int take,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.TelegramPosts
            .Include(x => x.Channel)
            .Include(x => x.CollectorAccount)
            .Where(x => x.ChannelId == channelId)
            .Where(x =>
                (
                    dbContext.TelegramPostDeliveries.Any(d =>
                        d.PostId == x.Id &&
                        d.DestinationKind == destinationKind &&
                        d.DestinationChatId == destinationChatId) &&
                    dbContext.TelegramPostRevisions.Where(r => r.PostId == x.Id).Max(r => (int?)r.RevisionNumber) >
                    dbContext.TelegramPostDeliveries
                        .Where(d =>
                            d.PostId == x.Id &&
                            d.DestinationKind == destinationKind &&
                            d.DestinationChatId == destinationChatId)
                        .Max(d => (int?)d.RevisionNumber)
                ) ||
                (
                    !dbContext.TelegramPostDeliveries.Any(d =>
                        d.PostId == x.Id &&
                        d.DestinationKind == destinationKind &&
                        d.DestinationChatId == destinationChatId) &&
                    x.IsEdited &&
                    x.TelegramMessageId <= deliveredThroughTelegramMessageId
                ))
            .OrderBy(x => x.UpdatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(TelegramPostDelivery delivery, CancellationToken cancellationToken = default) =>
        dbContext.TelegramPostDeliveries.AddAsync(delivery, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
