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

    public async Task RemoveAllTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var activeSubscriptions = await subscriptionRepository.GetActiveByUserTelegramIdAsync(telegramUserId, cancellationToken);
        foreach (var subscription in activeSubscriptions)
        {
            subscription.IsActive = false;
            subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await subscriptionRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChannelDto>> ListTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var channels = await trackedChannelRepository.GetChannelsForUserAsync(telegramUserId, cancellationToken);
        return channels.Select(ToDto).ToList();
    }

    private static ChannelDto ToDto(TrackedChannel channel) =>
        new(channel.Id, channel.ChannelName, channel.UsernameOrInviteLink, channel.Status.ToString(), channel.LastPostCollectedAtUtc, channel.LastCollectorError);
}
