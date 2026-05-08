using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IPostRevisionRepository
{
    Task<bool> AnyForPostAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<int> GetNextRevisionNumberAsync(Guid postId, CancellationToken cancellationToken = default);
    Task<TelegramPostRevision?> GetLatestByPostIdAsync(Guid postId, CancellationToken cancellationToken = default);
    Task AddAsync(TelegramPostRevision revision, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
