using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Application.Services;

public sealed class ChannelTrackingService(
    IUserService userService,
    ITrackedChannelRepository trackedChannelRepository,
    ISubscriptionRepository subscriptionRepository,
    ICollectorAccountRepository collectorAccountRepository,
    IPostRepository postRepository,
    IChannelKeyNormalizer channelKeyNormalizer) : IChannelTrackingService
{
    public async Task<ChannelDto> AddTrackedChannelAsync(AddTrackedChannelDto request, CancellationToken cancellationToken = default)
    {
        var user = await userService.UpsertTelegramUserAsync(
            new BotUserSnapshotDto(request.TelegramUserId, request.TelegramUsername, request.DisplayName, "en"),
            cancellationToken);

        var normalizedKey = channelKeyNormalizer.Normalize(request.ChannelReference);
        var channel = await trackedChannelRepository.GetByNormalizedKeyAsync(normalizedKey, cancellationToken);
        if (channel is null)
        {
            channel = new TrackedChannel
            {
                ChannelName = normalizedKey,
                UsernameOrInviteLink = request.ChannelReference.Trim(),
                NormalizedChannelKey = normalizedKey,
                Status = ChannelTrackingStatus.PendingSubscription
            };

            await trackedChannelRepository.AddAsync(channel, cancellationToken);

            var collector = await collectorAccountRepository.GetPrimaryAvailableAsync(cancellationToken);
            if (collector is not null)
            {
                var assignment = new ChannelCollectorAssignment
                {
                    Channel = channel,
                    CollectorAccountId = collector.Id,
                    Status = ChannelTrackingStatus.PendingSubscription,
                    IsPrimary = true
                };

                await collectorAccountRepository.AddAssignmentAsync(assignment, cancellationToken);
            }
        }

        var subscription = await subscriptionRepository.GetAsync(user.Id, channel.Id, cancellationToken);
        if (subscription is null)
        {
            var latestKnownMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(channel.Id, cancellationToken);
            subscription = new UserChannelSubscription
            {
                UserId = user.Id,
                ChannelId = channel.Id,
                IsActive = true,
                LastDeliveredTelegramMessageId = latestKnownMessageId
            };

            await subscriptionRepository.AddAsync(subscription, cancellationToken);
        }
        else
        {
            subscription.IsActive = true;
            subscription.LastDeliveredTelegramMessageId ??= await postRepository.GetLatestTelegramMessageIdForChannelAsync(channel.Id, cancellationToken);
            subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await trackedChannelRepository.SaveChangesAsync(cancellationToken);
        await subscriptionRepository.SaveChangesAsync(cancellationToken);

        return ToDto(channel);
    }

    public async Task RemoveTrackedChannelAsync(RemoveTrackedChannelDto request, CancellationToken cancellationToken = default)
    {
        var channels = await trackedChannelRepository.GetChannelsForUserAsync(request.TelegramUserId, cancellationToken);
        var normalizedKey = channelKeyNormalizer.Normalize(request.ChannelReference);
        var channel = channels.FirstOrDefault(x => x.NormalizedChannelKey == normalizedKey);
        if (channel is null)
        {
            return;
        }

        var activeSubscriptions = await subscriptionRepository.GetActiveByUserTelegramIdAsync(request.TelegramUserId, cancellationToken);
        var target = activeSubscriptions.FirstOrDefault(x => x.ChannelId == channel.Id);
        if (target is null)
        {
            return;
        }

        target.IsActive = false;
        target.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveTrackedChannelByIdAsync(RemoveTrackedChannelByIdDto request, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(request.TelegramUserId, cancellationToken);
        var target = subscriptions.FirstOrDefault(x => x.ChannelId == request.ChannelId);
        if (target is null)
        {
            return false;
        }

        subscriptionRepository.Remove(target);
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RemoveAllTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            subscriptionRepository.Remove(subscription);
        }

        await subscriptionRepository.SaveChangesAsync(cancellationToken);
        return subscriptions.Count;
    }

    public async Task<int> SetSubscriptionsActiveAsync(long telegramUserId, bool isActive, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var updatedCount = 0;
        foreach (var subscription in subscriptions.Where(x => x.IsActive != isActive))
        {
            subscription.IsActive = isActive;
            subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
            updatedCount++;
        }

        if (updatedCount > 0)
        {
            await subscriptionRepository.SaveChangesAsync(cancellationToken);
        }

        return updatedCount;
    }

    public async Task<IReadOnlyList<ChannelDto>> ListTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var channels = await trackedChannelRepository.GetChannelsForUserAsync(telegramUserId, cancellationToken);
        return channels.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        return subscriptions
            .Select(x => new SubscriptionDto(
                x.ChannelId,
                x.Channel.ChannelName,
                x.Channel.UsernameOrInviteLink,
                x.Channel.Status.ToString(),
                x.IsActive))
            .ToList();
    }

    private static ChannelDto ToDto(TrackedChannel channel) =>
        new(channel.Id, channel.ChannelName, channel.UsernameOrInviteLink, channel.Status.ToString(), channel.LastPostCollectedAtUtc, channel.LastCollectorError);
}
