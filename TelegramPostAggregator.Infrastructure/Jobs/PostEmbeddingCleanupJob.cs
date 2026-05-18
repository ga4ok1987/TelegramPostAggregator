using Hangfire;
using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 0)]
public sealed class PostEmbeddingCleanupJob(IPostEmbeddingService postEmbeddingService)
{
    public Task RunAsync(CancellationToken cancellationToken = default) =>
        postEmbeddingService.CleanupExpiredAsync(cancellationToken);
}
