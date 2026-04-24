using Hangfire;
using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Infrastructure.Jobs;

[DisableConcurrentExecution(300)]
public sealed class CollectorPostSyncJob(ICollectorCoordinator coordinator)
{
    public Task RunAsync(CancellationToken cancellationToken = default) =>
        coordinator.SynchronizePostsAsync(cancellationToken);
}
