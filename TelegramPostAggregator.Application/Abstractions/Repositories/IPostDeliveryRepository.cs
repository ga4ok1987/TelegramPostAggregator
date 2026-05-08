using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IPostDeliveryRepository
{
    Task<TelegramPostDelivery?> GetLatestForPostAndDestinationAsync(
        Guid postId,
        PostDeliveryDestinationKind destinationKind,
        long destinationChatId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TelegramPost>> GetPendingEditedPostsForDestinationAsync(
        Guid channelId,
        PostDeliveryDestinationKind destinationKind,
        long destinationChatId,
        long deliveredThroughTelegramMessageId,
        int take,
        CancellationToken cancellationToken = default);

    Task AddAsync(TelegramPostDelivery delivery, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
