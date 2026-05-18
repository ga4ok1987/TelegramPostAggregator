namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IPostEmbeddingService
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default);
}
