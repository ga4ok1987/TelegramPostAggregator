using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IChannelTrackingService
{
    Task<ChannelDto> AddTrackedChannelAsync(AddTrackedChannelDto request, CancellationToken cancellationToken = default);
    Task RemoveTrackedChannelAsync(RemoveTrackedChannelDto request, CancellationToken cancellationToken = default);
    Task<bool> RemoveTrackedChannelByIdAsync(RemoveTrackedChannelByIdDto request, CancellationToken cancellationToken = default);
    Task<int> RemoveAllTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<int> SetSubscriptionsActiveAsync(long telegramUserId, bool isActive, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChannelDto>> ListTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(long telegramUserId, CancellationToken cancellationToken = default);
}
