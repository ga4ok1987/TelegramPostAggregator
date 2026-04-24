using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface ISubscriptionRepository
{
    Task<UserChannelSubscription?> GetAsync(Guid userId, Guid channelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserChannelSubscription>> GetActiveByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserChannelSubscription>> GetActiveForDeliveryAsync(int take, CancellationToken cancellationToken = default);
    Task AddAsync(UserChannelSubscription subscription, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
