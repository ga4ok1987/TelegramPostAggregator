using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IClientAdminService
{
    Task<IReadOnlyList<AdminClientDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<AdminClientDetailDto?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AdminPagedResultDto<AdminBotSubscriptionDto>> GetBotSubscriptionsPageAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminPagedResultDto<AdminManagedChannelSourceSubscriptionDto>> GetManagedChannelSubscriptionsPageAsync(Guid managedChannelId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> CreateBotSubscriptionAsync(Guid userId, string channelReference, CancellationToken cancellationToken = default);
    Task<bool> SetBotSubscriptionActiveAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> DeleteBotSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    Task<bool> SetManagedChannelActiveAsync(Guid managedChannelId, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> DeleteManagedChannelAsync(Guid managedChannelId, CancellationToken cancellationToken = default);
    Task<bool> CreateManagedChannelSubscriptionAsync(Guid managedChannelId, string channelReference, CancellationToken cancellationToken = default);
    Task<bool> SetManagedChannelSubscriptionActiveAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> DeleteManagedChannelSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    Task<bool> SetBlockedAsync(Guid userId, bool isBlocked, CancellationToken cancellationToken = default);
    Task<bool> SetExtraSubscriptionSlotsAsync(Guid userId, int extraSubscriptionSlots, CancellationToken cancellationToken = default);
    Task<bool> SetExtraManagedChannelSlotsAsync(Guid userId, int extraManagedChannelSlots, CancellationToken cancellationToken = default);
}
