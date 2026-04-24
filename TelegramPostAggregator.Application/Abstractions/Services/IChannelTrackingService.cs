using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IChannelTrackingService
{
    Task<ChannelDto> AddTrackedChannelAsync(AddTrackedChannelDto request, CancellationToken cancellationToken = default);
    Task RemoveTrackedChannelAsync(RemoveTrackedChannelDto request, CancellationToken cancellationToken = default);
    Task RemoveAllTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChannelDto>> ListTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default);
}
