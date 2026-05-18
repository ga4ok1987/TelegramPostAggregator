using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using System.Text.RegularExpressions;

namespace TelegramPostAggregator.Application.Services;

public sealed class ChannelTrackingService(
    IUserService userService,
    IBillingService billingService,
    ITrackedChannelRepository trackedChannelRepository,
    ISubscriptionRepository subscriptionRepository,
    IManagedChannelRepository managedChannelRepository,
    IManagedChannelSubscriptionRepository managedChannelSubscriptionRepository,
    ICollectorAccountRepository collectorAccountRepository,
    IPostRepository postRepository,
    IChannelKeyNormalizer channelKeyNormalizer,
    Bot.BotLocalizationCatalog localizationCatalog) : IChannelTrackingService
{
    private static readonly Regex TelegramUsernameRegex = new("^[A-Za-z0-9_]{4,}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ReservedTelegramPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "addstickers",
        "boost",
        "c",
        "faq",
        "giftcode",
        "invoice",
        "joinchat",
        "login",
        "m",
        "proxy",
        "s",
        "share",
        "stickers",
        "username"
    };

    public async Task<ChannelTrackingResultDto> AddTrackedChannelAsync(AddTrackedChannelDto request, CancellationToken cancellationToken = default)
    {
        var user = await userService.UpsertTelegramUserAsync(
            new BotUserSnapshotDto(request.TelegramUserId, request.TelegramUsername, request.DisplayName, "en"),
            cancellationToken);

        if (!TryValidateChannelReference(request.ChannelReference, out var validationError))
        {
            var currentUsage = await billingService.GetSubscriptionUsageAsync(request.TelegramUserId, cancellationToken);
            return new ChannelTrackingResultDto(false, validationError!, Usage: currentUsage);
        }

        var channel = await EnsureTrackedChannelAsync(request.ChannelReference, cancellationToken);
        var billingDecision = await billingService.CanAddChannelAsync(request.TelegramUserId, channel.Id, cancellationToken);
        if (!billingDecision.Success)
        {
            return billingDecision with { Channel = ToDto(channel) };
        }

        var subscription = await subscriptionRepository.GetAsync(user.Id, channel.Id, cancellationToken);
        var canActivateSubscription = channel.Status == ChannelTrackingStatus.Active;
        if (subscription is null)
        {
            subscription = new UserChannelSubscription
            {
                UserId = user.Id,
                ChannelId = channel.Id,
                IsActive = canActivateSubscription
            };

            if (canActivateSubscription)
            {
                subscription.LastDeliveredTelegramMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(channel.Id, cancellationToken);
            }

            await subscriptionRepository.AddAsync(subscription, cancellationToken);
        }
        else
        {
            subscription.IsActive = canActivateSubscription;
            if (canActivateSubscription)
            {
                subscription.LastDeliveredTelegramMessageId ??= await postRepository.GetLatestTelegramMessageIdForChannelAsync(channel.Id, cancellationToken);
            }

            subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await trackedChannelRepository.SaveChangesAsync(cancellationToken);
        await subscriptionRepository.SaveChangesAsync(cancellationToken);

        var usage = await billingService.GetSubscriptionUsageAsync(request.TelegramUserId, cancellationToken);
        return new ChannelTrackingResultDto(true, string.Empty, ToDto(channel), usage);
    }

    public async Task<ManagedChannelTrackingResultDto> AddTrackedChannelToManagedChannelAsync(
        AddManagedChannelTrackedChannelDto request,
        CancellationToken cancellationToken = default)
    {
        var managedChannel = await managedChannelRepository.GetByTelegramChatIdAsync(request.ManagedChannelChatId, cancellationToken);
        if (managedChannel is null)
        {
            return new ManagedChannelTrackingResultDto(false, "Connect this destination channel first from the bot.");
        }

        if (!TryValidateChannelReference(request.ChannelReference, out var validationError))
        {
            return new ManagedChannelTrackingResultDto(false, validationError!);
        }

        var channel = await EnsureTrackedChannelAsync(request.ChannelReference, cancellationToken);
        var billingDecision = await billingService.CanAddChannelAsync(managedChannel.User.TelegramUserId, channel.Id, cancellationToken);
        if (!billingDecision.Success)
        {
            return new ManagedChannelTrackingResultDto(false, billingDecision.Message, ToDto(channel));
        }

        var subscription = await managedChannelSubscriptionRepository.GetAsync(managedChannel.Id, channel.Id, cancellationToken);
        if (subscription is null)
        {
            var latestKnownMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(channel.Id, cancellationToken);
            subscription = new ManagedChannelSubscription
            {
                ManagedChannelId = managedChannel.Id,
                ChannelId = channel.Id,
                IsActive = true,
                LastDeliveredTelegramMessageId = latestKnownMessageId
            };

            await managedChannelSubscriptionRepository.AddAsync(subscription, cancellationToken);
        }
        else
        {
            subscription.IsActive = true;
            subscription.LastDeliveredTelegramMessageId ??= await postRepository.GetLatestTelegramMessageIdForChannelAsync(channel.Id, cancellationToken);
            subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await trackedChannelRepository.SaveChangesAsync(cancellationToken);
        await managedChannelSubscriptionRepository.SaveChangesAsync(cancellationToken);

        return new ManagedChannelTrackingResultDto(
            true,
            $"Monitoring enabled for {channel.UsernameOrInviteLink}. New posts will be delivered to this channel.",
            ToDto(channel));
    }

    public async Task RemoveTrackedChannelAsync(RemoveTrackedChannelDto request, CancellationToken cancellationToken = default)
    {
        var normalizedKey = channelKeyNormalizer.Normalize(request.ChannelReference);
        var subscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(request.TelegramUserId, cancellationToken);
        var target = subscriptions.FirstOrDefault(x => x.Channel.NormalizedChannelKey == normalizedKey);
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
        var managedSubscriptions = await managedChannelSubscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var managedChannels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var updatedCount = 0;
        foreach (var subscription in subscriptions.Where(x => x.Channel.Status == ChannelTrackingStatus.Active && x.IsActive != isActive))
        {
            subscription.IsActive = isActive;
            if (isActive)
            {
                subscription.LastDeliveredTelegramMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(subscription.ChannelId, cancellationToken);
            }
            subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
            updatedCount++;
        }

        foreach (var subscription in managedSubscriptions.Where(x => x.IsActive != isActive))
        {
            subscription.IsActive = isActive;
            if (isActive)
            {
                subscription.LastDeliveredTelegramMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(subscription.ChannelId, cancellationToken);
            }

            subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
            updatedCount++;
        }

        foreach (var channel in managedChannels.Where(x => x.IsActive != isActive))
        {
            channel.IsActive = isActive;
            channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
            updatedCount++;
        }

        if (updatedCount > 0)
        {
            await subscriptionRepository.SaveChangesAsync(cancellationToken);
            await managedChannelSubscriptionRepository.SaveChangesAsync(cancellationToken);
            await managedChannelRepository.SaveChangesAsync(cancellationToken);
        }

        return updatedCount;
    }

    public async Task<IReadOnlyList<ChannelDto>> ListTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var channels = await trackedChannelRepository.GetChannelsForUserAsync(telegramUserId, cancellationToken);
        return channels
            .Where(x => !IsReservedUiChannel(x.ChannelName, x.UsernameOrInviteLink))
            .Select(ToDto)
            .ToList();
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetActiveByUserTelegramIdAsync(telegramUserId, cancellationToken);
        return subscriptions
            .Where(x => !IsReservedUiChannel(x.Channel.ChannelName, x.Channel.UsernameOrInviteLink))
            .Select(x => new SubscriptionDto(
                x.ChannelId,
                x.Channel.ChannelName,
                x.Channel.UsernameOrInviteLink,
                x.Channel.Status.ToString(),
                x.IsActive))
            .ToList();
    }

    private bool IsReservedUiChannel(string channelName, string channelReference) =>
        localizationCatalog.IsReservedUiText(channelName) || localizationCatalog.IsReservedUiText(channelReference);

    private async Task<TrackedChannel> EnsureTrackedChannelAsync(string channelReference, CancellationToken cancellationToken)
    {
        var normalizedKey = channelKeyNormalizer.Normalize(channelReference);
        var channel = await trackedChannelRepository.GetByNormalizedKeyAsync(normalizedKey, cancellationToken);
        if (channel is not null)
        {
            return channel;
        }

        var normalizedReference = NormalizeChannelReference(channelReference, normalizedKey);
        channel = new TrackedChannel
        {
            ChannelName = normalizedKey,
            UsernameOrInviteLink = normalizedReference,
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

        return channel;
    }

    private static ChannelDto ToDto(TrackedChannel channel) =>
        new(channel.Id, channel.ChannelName, channel.UsernameOrInviteLink, channel.Status.ToString(), channel.LastPostCollectedAtUtc, channel.LastCollectorError);

    private static string NormalizeChannelReference(string channelReference, string normalizedKey)
    {
        var trimmed = channelReference.Trim().TrimEnd('\'', '"', ',', '.', ';', ':', '!', '?', ')', ']', '}');
        if (trimmed.Contains("/+", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("joinchat/", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + trimmed["http://".Length..];
            }

            if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + trimmed;
            }

            return trimmed.StartsWith("+", StringComparison.Ordinal)
                ? $"https://t.me/{trimmed}"
                : $"https://t.me/{trimmed.TrimStart('/')}";
        }

        if (trimmed.StartsWith("@", StringComparison.Ordinal))
        {
            return "@" + normalizedKey;
        }

        if (trimmed.StartsWith("https://t.me/s/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("http://t.me/s/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("t.me/s/", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://t.me/{normalizedKey}";
        }

        if (trimmed.StartsWith("+", StringComparison.Ordinal))
        {
            return $"https://t.me/{trimmed}";
        }

        return $"https://t.me/{normalizedKey}";
    }

    private static bool TryValidateChannelReference(string channelReference, out string? validationError)
    {
        validationError = null;
        var trimmed = channelReference.Trim().TrimEnd('\'', '"', ',', '.', ';', ':', '!', '?', ')', ']', '}');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            validationError = "Send a Telegram channel link or @username.";
            return false;
        }

        if (trimmed.StartsWith("+", StringComparison.Ordinal) ||
            trimmed.Contains("/+", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("joinchat/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.StartsWith("@", StringComparison.Ordinal))
        {
            var username = trimmed[1..];
            if (TelegramUsernameRegex.IsMatch(username))
            {
                return true;
            }

            validationError = "Send a valid Telegram channel username like @channel_name.";
            return false;
        }

        var candidate = trimmed;
        if (candidate.StartsWith("https://t.me/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["https://t.me/".Length..];
        }
        else if (candidate.StartsWith("http://t.me/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["http://t.me/".Length..];
        }
        else if (candidate.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["t.me/".Length..];
        }

        candidate = candidate.Trim('/');
        if (candidate.StartsWith("s/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["s/".Length..].Trim('/');
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            validationError = "Send a Telegram channel link or @username.";
            return false;
        }

        var slashIndex = candidate.IndexOf('/');
        var slug = slashIndex >= 0 ? candidate[..slashIndex] : candidate;
        if (ReservedTelegramPaths.Contains(slug))
        {
            validationError = "This Telegram link is not a monitorable channel. Send a channel link or @username.";
            return false;
        }

        if (!TelegramUsernameRegex.IsMatch(slug))
        {
            validationError = "Send a valid Telegram channel link like https://t.me/channel_name or @channel_name.";
            return false;
        }

        return true;
    }
}
