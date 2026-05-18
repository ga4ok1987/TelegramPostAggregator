using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface ITelegramPostEmbeddingRepository
{
    Task<TelegramPostEmbedding?> GetByPostIdAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelegramPostEmbedding>> GetExpiredBatchAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TelegramPostEmbedding embedding, CancellationToken cancellationToken = default);
    void Remove(TelegramPostEmbedding embedding);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
