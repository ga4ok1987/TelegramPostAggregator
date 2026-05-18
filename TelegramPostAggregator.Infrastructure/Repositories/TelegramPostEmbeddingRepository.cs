using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class TelegramPostEmbeddingRepository(AggregatorDbContext dbContext) : ITelegramPostEmbeddingRepository
{
    public Task<TelegramPostEmbedding?> GetByPostIdAsync(Guid postId, CancellationToken cancellationToken = default) =>
        dbContext.Set<TelegramPostEmbedding>()
            .FirstOrDefaultAsync(x => x.PostId == postId, cancellationToken);

    public async Task<IReadOnlyList<TelegramPostEmbedding>> GetExpiredBatchAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default) =>
        await dbContext.Set<TelegramPostEmbedding>()
            .Include(x => x.Post)
            .Where(x => x.ExpiresAtUtc <= nowUtc)
            .OrderBy(x => x.ExpiresAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        dbContext.Set<TelegramPostEmbedding>().CountAsync(cancellationToken);

    public Task AddAsync(TelegramPostEmbedding embedding, CancellationToken cancellationToken = default) =>
        dbContext.Set<TelegramPostEmbedding>().AddAsync(embedding, cancellationToken).AsTask();

    public void Remove(TelegramPostEmbedding embedding) =>
        dbContext.Set<TelegramPostEmbedding>().Remove(embedding);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
