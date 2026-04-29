using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IMiniAppChannelService
{
    Task<IReadOnlyList<MiniAppChannelDto>> ListAsync(long telegramUserId, CancellationToken cancellationToken = default);
    Task<ManagedChannelRegistrationResultDto> RegisterSharedChannelAsync(long telegramUserId, TelegramSharedChatDto sharedChat, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(long telegramUserId, Guid channelId, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long telegramUserId, Guid channelId, CancellationToken cancellationToken = default);
    Task<bool> SetSubscriptionActiveAsync(long telegramUserId, Guid managedChannelId, Guid subscriptionId, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> DeleteSubscriptionAsync(long telegramUserId, Guid managedChannelId, Guid subscriptionId, CancellationToken cancellationToken = default);
}
