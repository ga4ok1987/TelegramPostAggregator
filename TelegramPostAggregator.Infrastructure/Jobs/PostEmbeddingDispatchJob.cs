using Hangfire;
using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 0)]
public sealed class PostEmbeddingDispatchJob(IPostEmbeddingService postEmbeddingService)
{
    public Task RunAsync(CancellationToken cancellationToken = default) =>
        postEmbeddingService.ProcessPendingAsync(cancellationToken);
}
