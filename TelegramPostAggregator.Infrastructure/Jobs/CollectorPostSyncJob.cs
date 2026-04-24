using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Infrastructure.Jobs;

public sealed class CollectorPostSyncJob(ICollectorCoordinator coordinator)
{
    public Task RunAsync(CancellationToken cancellationToken = default) =>
        coordinator.SynchronizePostsAsync(cancellationToken);
}
