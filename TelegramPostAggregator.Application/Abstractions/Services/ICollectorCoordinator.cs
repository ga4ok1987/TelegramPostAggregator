namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface ICollectorCoordinator
{
    Task ProcessPendingSubscriptionsAsync(CancellationToken cancellationToken = default);
    Task SynchronizePostsAsync(CancellationToken cancellationToken = default);
}
