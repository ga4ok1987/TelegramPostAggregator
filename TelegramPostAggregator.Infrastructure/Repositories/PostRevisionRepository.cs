using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class PostRevisionRepository(AggregatorDbContext dbContext) : IPostRevisionRepository
{
    public Task<bool> AnyForPostAsync(Guid postId, CancellationToken cancellationToken = default) =>
        dbContext.TelegramPostRevisions.AnyAsync(x => x.PostId == postId, cancellationToken);

    public async Task<int> GetNextRevisionNumberAsync(Guid postId, CancellationToken cancellationToken = default) =>
        (await dbContext.TelegramPostRevisions
            .Where(x => x.PostId == postId)
            .MaxAsync(x => (int?)x.RevisionNumber, cancellationToken) ?? 0) + 1;

    public Task<TelegramPostRevision?> GetLatestByPostIdAsync(Guid postId, CancellationToken cancellationToken = default) =>
        dbContext.TelegramPostRevisions
            .Where(x => x.PostId == postId)
            .OrderByDescending(x => x.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public Task AddAsync(TelegramPostRevision revision, CancellationToken cancellationToken = default) =>
        dbContext.TelegramPostRevisions.AddAsync(revision, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
