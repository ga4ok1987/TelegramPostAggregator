using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IPostRepository
{
    Task<TelegramPost?> GetByChannelAndMessageIdAsync(Guid channelId, long telegramMessageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, TelegramPost>> GetByChannelAndMessageIdsAsync(Guid channelId, IReadOnlyCollection<long> telegramMessageIds, CancellationToken cancellationToken = default);
    Task<TelegramPost?> GetByIdAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelegramPost>> GetFeedForUserAsync(long telegramUserId, int take, int skip, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelegramPost>> GetUndeliveredForChannelAsync(Guid channelId, long? lastDeliveredTelegramMessageId, int take, CancellationToken cancellationToken = default);
    Task<long?> GetLatestTelegramMessageIdForChannelAsync(Guid channelId, CancellationToken cancellationToken = default);
    Task AddAsync(TelegramPost post, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
