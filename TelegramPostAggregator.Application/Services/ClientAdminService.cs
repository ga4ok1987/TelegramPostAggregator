using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services;

public sealed class ClientAdminService(
    IAppUserRepository appUserRepository,
    ISubscriptionRepository subscriptionRepository,
    IManagedChannelRepository managedChannelRepository,
    IManagedChannelSubscriptionRepository managedChannelSubscriptionRepository,
    IChannelTrackingService channelTrackingService,
    IMiniAppChannelService miniAppChannelService,
    IPostRepository postRepository,
    IBillingService billingService) : IClientAdminService
{
    public async Task<IReadOnlyList<AdminClientDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await appUserRepository.ListForAdminAsync(cancellationToken);

        return users
            .Select(MapSummary)
            .ToArray();
    }

    public async Task<AdminClientDetailDto?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await appUserRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var usage = await billingService.GetSubscriptionUsageAsync(user.TelegramUserId, cancellationToken);
        return MapDetail(user, usage);
    }

    public async Task<AdminPagedResultDto<AdminBotSubscriptionDto>> GetBotSubscriptionsPageAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Max(pageSize, 1);
        var skip = (normalizedPage - 1) * normalizedPageSize;
        var totalCount = await subscriptionRepository.CountByUserIdAsync(userId, cancellationToken);
        var subscriptions = await subscriptionRepository.GetPageByUserIdAsync(userId, skip, normalizedPageSize, cancellationToken);

        return new AdminPagedResultDto<AdminBotSubscriptionDto>(
            subscriptions.Select(subscription => new AdminBotSubscriptionDto(
                    subscription.Id,
                    subscription.ChannelId,
                    subscription.Channel.ChannelName,
                    subscription.Channel.UsernameOrInviteLink,
                    subscription.IsActive,
                    subscription.LastDeliveredAtUtc))
                .ToArray(),
            normalizedPage,
            normalizedPageSize,
            totalCount);
    }

    public async Task<AdminPagedResultDto<AdminManagedChannelSourceSubscriptionDto>> GetManagedChannelSubscriptionsPageAsync(Guid managedChannelId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Max(pageSize, 1);
        var skip = (normalizedPage - 1) * normalizedPageSize;
        var totalCount = await managedChannelSubscriptionRepository.CountByManagedChannelIdAsync(managedChannelId, cancellationToken);
        var subscriptions = await managedChannelSubscriptionRepository.GetPageByManagedChannelIdAsync(managedChannelId, skip, normalizedPageSize, cancellationToken);

        return new AdminPagedResultDto<AdminManagedChannelSourceSubscriptionDto>(
            subscriptions.Select(subscription => new AdminManagedChannelSourceSubscriptionDto(
                    subscription.Id,
                    subscription.ChannelId,
                    subscription.Channel.ChannelName,
                    subscription.Channel.UsernameOrInviteLink,
                    subscription.IsActive,
                    subscription.LastDeliveredAtUtc))
                .ToArray(),
            normalizedPage,
            normalizedPageSize,
            totalCount);
    }

    public async Task<bool> CreateBotSubscriptionAsync(Guid userId, string channelReference, CancellationToken cancellationToken = default)
    {
        var user = await appUserRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(channelReference))
        {
            return false;
        }

        var result = await channelTrackingService.AddTrackedChannelAsync(
            new AddTrackedChannelDto(
                user.TelegramUserId,
                user.TelegramUsername,
                user.DisplayName,
                channelReference.Trim()),
            cancellationToken);

        return result.Success;
    }

    public async Task<bool> SetBotSubscriptionActiveAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken = default)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        if (subscription.IsActive == isActive)
        {
            return true;
        }

        subscription.IsActive = isActive;
        if (isActive)
        {
            subscription.LastDeliveredTelegramMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(subscription.ChannelId, cancellationToken);
        }

        subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteBotSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        subscriptionRepository.Remove(subscription);
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetManagedChannelActiveAsync(Guid managedChannelId, bool isActive, CancellationToken cancellationToken = default)
    {
        var managedChannel = await managedChannelRepository.GetByIdAsync(managedChannelId, cancellationToken);
        if (managedChannel is null)
        {
            return false;
        }

        return await miniAppChannelService.SetActiveAsync(managedChannel.User.TelegramUserId, managedChannelId, isActive, cancellationToken);
    }

    public async Task<bool> DeleteManagedChannelAsync(Guid managedChannelId, CancellationToken cancellationToken = default)
    {
        var managedChannel = await managedChannelRepository.GetByIdAsync(managedChannelId, cancellationToken);
        if (managedChannel is null)
        {
            return false;
        }

        return await miniAppChannelService.DeleteAsync(managedChannel.User.TelegramUserId, managedChannelId, cancellationToken);
    }

    public async Task<bool> CreateManagedChannelSubscriptionAsync(Guid managedChannelId, string channelReference, CancellationToken cancellationToken = default)
    {
        var managedChannel = await managedChannelRepository.GetByIdAsync(managedChannelId, cancellationToken);
        if (managedChannel is null || string.IsNullOrWhiteSpace(channelReference))
        {
            return false;
        }

        var result = await channelTrackingService.AddTrackedChannelToManagedChannelAsync(
            new AddManagedChannelTrackedChannelDto(managedChannel.TelegramChatId, channelReference.Trim()),
            cancellationToken);

        return result.Success;
    }

    public async Task<bool> SetManagedChannelSubscriptionActiveAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken = default)
    {
        var subscription = await managedChannelSubscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        return await miniAppChannelService.SetSubscriptionActiveAsync(
            subscription.ManagedChannel.User.TelegramUserId,
            subscription.ManagedChannelId,
            subscriptionId,
            isActive,
            cancellationToken);
    }

    public async Task<bool> DeleteManagedChannelSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await managedChannelSubscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        return await miniAppChannelService.DeleteSubscriptionAsync(
            subscription.ManagedChannel.User.TelegramUserId,
            subscription.ManagedChannelId,
            subscriptionId,
            cancellationToken);
    }

    public async Task<bool> SetBlockedAsync(Guid userId, bool isBlocked, CancellationToken cancellationToken = default)
    {
        var user = await appUserRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        if (user.IsBlockedBot == isBlocked)
        {
            return true;
        }

        user.IsBlockedBot = isBlocked;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await appUserRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetExtraSubscriptionSlotsAsync(Guid userId, int extraSubscriptionSlots, CancellationToken cancellationToken = default)
    {
        var user = await appUserRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        var normalizedValue = Math.Max(extraSubscriptionSlots, 0);
        if (user.ExtraSubscriptionSlots == normalizedValue)
        {
            return true;
        }

        user.ExtraSubscriptionSlots = normalizedValue;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await appUserRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static AdminClientDto MapSummary(Domain.Entities.AppUser user)
    {
        var managedChannelsCount = user.ManagedChannels.Count;
        var activeManagedChannelsCount = user.ManagedChannels.Count(channel => channel.IsActive);
        var botSubscriptionsCount = user.ChannelSubscriptions.Count;
        var activeBotSubscriptionsCount = user.ChannelSubscriptions.Count(subscription => subscription.IsActive);

        return new AdminClientDto(
            user.Id,
            user.TelegramUserId,
            user.TelegramUsername,
            user.DisplayName,
            user.PreferredLanguageCode,
            user.IsBlockedBot,
            user.CreatedAtUtc,
            managedChannelsCount,
            activeManagedChannelsCount,
            botSubscriptionsCount,
            activeBotSubscriptionsCount);
    }

    private static AdminClientDetailDto MapDetail(Domain.Entities.AppUser user, SubscriptionUsageDto usage)
    {
        var channels = user.ManagedChannels
            .OrderByDescending(channel => channel.IsActive)
            .ThenBy(channel => channel.ChannelName)
            .Select(channel => new AdminClientChannelDto(
                channel.Id,
                channel.ChannelName,
                !string.IsNullOrWhiteSpace(channel.Username) ? $"@{channel.Username}" : channel.TelegramChatId.ToString(),
                channel.IsActive,
                channel.SourceSubscriptions.Count,
                channel.SourceSubscriptions.Count(subscription => subscription.IsActive),
                channel.LastWriteSucceededAtUtc,
                channel.LastWriteError))
            .ToArray();

        return new AdminClientDetailDto(
            user.Id,
            user.TelegramUserId,
            user.TelegramUsername,
            user.DisplayName,
            user.PreferredLanguageCode,
            user.IsBlockedBot,
            user.CreatedAtUtc,
            usage.CurrentPlanName,
            usage.ChannelLimit,
            usage.UsedChannels,
            user.ExtraSubscriptionSlots,
            usage.ExpiresAtUtc,
            channels.Length,
            channels.Count(channel => channel.IsActive),
            channels.Sum(channel => channel.SubscriptionCount),
            channels.Sum(channel => channel.ActiveSubscriptionCount),
            user.ChannelSubscriptions.Count,
            user.ChannelSubscriptions.Count(subscription => subscription.IsActive),
            channels);
    }
}
