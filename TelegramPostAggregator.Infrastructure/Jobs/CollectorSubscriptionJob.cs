using TelegramPostAggregator.Application.Abstractions.Services;

namespace TelegramPostAggregator.Infrastructure.Jobs;

public sealed class CollectorSubscriptionJob(ICollectorCoordinator coordinator)
{
    public Task RunAsync(CancellationToken cancellationToken = default) =>
        coordinator.ProcessPendingSubscriptionsAsync(cancellationToken);
}
