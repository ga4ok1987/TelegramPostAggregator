using Microsoft.Extensions.Logging;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Services;

public sealed class MiniAppChannelService(
    IManagedChannelRepository managedChannelRepository,
    IManagedChannelSubscriptionRepository managedChannelSubscriptionRepository,
    IAppUserRepository appUserRepository,
    IPostRepository postRepository,
    ITelegramBotGateway telegramBotGateway,
    Microsoft.Extensions.Logging.ILogger<MiniAppChannelService> logger) : IMiniAppChannelService
{
    public async Task<IReadOnlyList<MiniAppChannelDto>> ListAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var channels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        if (channels.Count == 0)
        {
            return [];
        }

        var subscriptionsByManagedChannelId = (await managedChannelSubscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken))
            .GroupBy(x => x.ManagedChannelId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ManagedChannelSubscription>)group
                    .OrderBy(x => x.Channel.ChannelName)
                    .ToList());

        var adminChecks = await Task.WhenAll(channels.Select(async channel => new
        {
            Channel = channel,
            IsBotAdministrator = await telegramBotGateway.IsBotAdministratorAsync(channel.TelegramChatId.ToString(), cancellationToken)
        }));

        var visibleChannels = adminChecks
            .Where(result => result.IsBotAdministrator)
            .Select(result => result.Channel)
            .OrderByDescending(channel => channel.IsActive)
            .ThenBy(channel => channel.ChannelName)
            .ToList();

        var channelDtos = await Task.WhenAll(visibleChannels.Select(channel =>
            BuildChannelDtoAsync(channel, subscriptionsByManagedChannelId, cancellationToken)));

        return channelDtos;
    }

    public async Task<ManagedChannelRegistrationResultDto> RegisterSharedChannelAsync(long telegramUserId, TelegramSharedChatDto sharedChat, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Registering shared channel. TelegramUserId={TelegramUserId}, SharedChatId={SharedChatId}, Title={Title}, Username={Username}",
            telegramUserId,
            sharedChat.ChatId,
            sharedChat.Title,
            sharedChat.Username);

        var user = await appUserRepository.GetByTelegramUserIdAsync(telegramUserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Shared channel registration aborted because app user was not found. TelegramUserId={TelegramUserId}", telegramUserId);
            return new ManagedChannelRegistrationResultDto(false, "Start the bot first, then add your channel again.");
        }

        var isBotAdministrator = await telegramBotGateway.IsBotAdministratorAsync(sharedChat.ChatId.ToString(), cancellationToken);
        if (!isBotAdministrator)
        {
            logger.LogWarning(
                "Shared channel registration aborted because bot is not administrator. TelegramUserId={TelegramUserId}, SharedChatId={SharedChatId}",
                telegramUserId,
                sharedChat.ChatId);
            return new ManagedChannelRegistrationResultDto(false, "The bot is not an administrator in this channel yet.");
        }

        var existing = await managedChannelRepository.GetByTelegramChatIdAsync(user.Id, sharedChat.ChatId, cancellationToken);
        var managedChannel = existing ?? new ManagedChannel
        {
            UserId = user.Id,
            TelegramChatId = sharedChat.ChatId
        };

        managedChannel.ChannelName = !string.IsNullOrWhiteSpace(sharedChat.Title)
            ? sharedChat.Title.Trim()
            : BuildFallbackChannelName(sharedChat);
        managedChannel.Username = NormalizeUsername(sharedChat.Username);
        managedChannel.IsActive = true;
        managedChannel.LastVerifiedAtUtc = DateTimeOffset.UtcNow;
        managedChannel.LastWriteError = null;

        var probeResult = await telegramBotGateway.SendMessageAsync(
            new TelegramBotOutboundMessageDto(
                sharedChat.ChatId,
                "Channels Monitor connected successfully.",
                ParseMode: null,
                DisableWebPagePreview: true),
            cancellationToken);

        managedChannel.LastVerifiedAtUtc = DateTimeOffset.UtcNow;

        if (!probeResult.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Shared channel registration probe failed. TelegramUserId={TelegramUserId}, SharedChatId={SharedChatId}, Response={ResponseBody}",
                telegramUserId,
                sharedChat.ChatId,
                probeResult.ResponseBody);
            managedChannel.LastWriteError = probeResult.ResponseBody ?? "The bot could not post to this channel.";
            managedChannel.IsActive = false;

            if (existing is null)
            {
                await managedChannelRepository.AddAsync(managedChannel, cancellationToken);
            }

            await managedChannelRepository.SaveChangesAsync(cancellationToken);
            return new ManagedChannelRegistrationResultDto(false, "The channel was found, but the bot could not post there.");
        }

        managedChannel.LastWriteSucceededAtUtc = DateTimeOffset.UtcNow;
        managedChannel.LastWriteError = null;

        if (existing is null)
        {
            await managedChannelRepository.AddAsync(managedChannel, cancellationToken);
        }

        await managedChannelRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Shared channel registration saved successfully. TelegramUserId={TelegramUserId}, SharedChatId={SharedChatId}, ManagedChannelId={ManagedChannelId}",
            telegramUserId,
            sharedChat.ChatId,
            managedChannel.Id);
        return new ManagedChannelRegistrationResultDto(true, $"Channel connected: {managedChannel.ChannelName}");
    }

    public async Task<bool> SetActiveAsync(long telegramUserId, Guid channelId, bool isActive, CancellationToken cancellationToken = default)
    {
        var channels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var target = channels.FirstOrDefault(channel => channel.Id == channelId);
        if (target is null)
        {
            return false;
        }

        if (target.IsActive == isActive)
        {
            return true;
        }

        target.IsActive = isActive;
        if (isActive)
        {
            var subscriptions = await managedChannelSubscriptionRepository.GetByManagedChannelIdAsync(target.Id, cancellationToken);
            foreach (var subscription in subscriptions)
            {
                subscription.IsActive = true;
                subscription.LastDeliveredTelegramMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(subscription.ChannelId, cancellationToken);
                subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            await managedChannelSubscriptionRepository.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var subscriptions = await managedChannelSubscriptionRepository.GetByManagedChannelIdAsync(target.Id, cancellationToken);
            foreach (var subscription in subscriptions.Where(x => x.IsActive))
            {
                subscription.IsActive = false;
                subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            await managedChannelSubscriptionRepository.SaveChangesAsync(cancellationToken);
        }

        target.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await managedChannelRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(long telegramUserId, Guid channelId, CancellationToken cancellationToken = default)
    {
        var channels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var target = channels.FirstOrDefault(channel => channel.Id == channelId);
        if (target is null)
        {
            return false;
        }

        managedChannelRepository.Remove(target);
        await managedChannelRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetSubscriptionActiveAsync(long telegramUserId, Guid managedChannelId, Guid subscriptionId, bool isActive, CancellationToken cancellationToken = default)
    {
        var channels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var managedChannel = channels.FirstOrDefault(channel => channel.Id == managedChannelId);
        if (managedChannel is null)
        {
            return false;
        }

        var subscriptions = await managedChannelSubscriptionRepository.GetByManagedChannelIdAsync(managedChannelId, cancellationToken);
        var target = subscriptions.FirstOrDefault(x => x.Id == subscriptionId);
        if (target is null)
        {
            return false;
        }

        if (target.IsActive == isActive)
        {
            return true;
        }

        target.IsActive = isActive;
        if (isActive)
        {
            target.LastDeliveredTelegramMessageId = await postRepository.GetLatestTelegramMessageIdForChannelAsync(target.ChannelId, cancellationToken);
        }

        target.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await managedChannelSubscriptionRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteSubscriptionAsync(long telegramUserId, Guid managedChannelId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var channels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var managedChannel = channels.FirstOrDefault(channel => channel.Id == managedChannelId);
        if (managedChannel is null)
        {
            return false;
        }

        var subscriptions = await managedChannelSubscriptionRepository.GetByManagedChannelIdAsync(managedChannelId, cancellationToken);
        var target = subscriptions.FirstOrDefault(x => x.Id == subscriptionId);
        if (target is null)
        {
            return false;
        }

        managedChannelSubscriptionRepository.Remove(target);
        await managedChannelSubscriptionRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string BuildChannelReference(ManagedChannel channel) =>
        !string.IsNullOrWhiteSpace(channel.Username)
            ? $"@{channel.Username.TrimStart('@')}"
            : channel.TelegramChatId.ToString();

    private static string BuildFallbackChannelName(TelegramSharedChatDto sharedChat) =>
        !string.IsNullOrWhiteSpace(sharedChat.Username)
            ? $"@{sharedChat.Username.TrimStart('@')}"
            : $"Channel {sharedChat.ChatId}";

    private static string? NormalizeUsername(string? username) =>
        string.IsNullOrWhiteSpace(username) ? null : username.Trim().TrimStart('@');

    private async Task<MiniAppChannelDto> BuildChannelDtoAsync(
        ManagedChannel channel,
        IReadOnlyDictionary<Guid, IReadOnlyList<ManagedChannelSubscription>> subscriptionsByManagedChannelId,
        CancellationToken cancellationToken)
    {
        var avatarImageUrl = await telegramBotGateway.GetChatProfileImageDataUrlAsync(channel.TelegramChatId.ToString(), cancellationToken);
        var subscriptions = await BuildSubscriptionDtosAsync(subscriptionsByManagedChannelId, channel.Id, cancellationToken);

        return new MiniAppChannelDto(
            channel.Id,
            channel.ChannelName,
            BuildChannelReference(channel),
            avatarImageUrl,
            channel.IsActive ? "Connected" : "Paused",
            channel.IsActive,
            channel.LastWriteSucceededAtUtc ?? channel.LastVerifiedAtUtc,
            channel.LastWriteError,
            subscriptions);
    }

    private async Task<IReadOnlyList<MiniAppSourceSubscriptionDto>> BuildSubscriptionDtosAsync(
        IReadOnlyDictionary<Guid, IReadOnlyList<ManagedChannelSubscription>> subscriptionsByManagedChannelId,
        Guid managedChannelId,
        CancellationToken cancellationToken)
    {
        if (!subscriptionsByManagedChannelId.TryGetValue(managedChannelId, out var subscriptions))
        {
            return [];
        }

        var subscriptionDtos = await Task.WhenAll(subscriptions.Select(async subscription =>
        {
            var avatarImageUrl = await telegramBotGateway.GetChatProfileImageDataUrlAsync(
                BuildTrackedChannelReference(subscription.Channel),
                cancellationToken);

            return new MiniAppSourceSubscriptionDto(
                subscription.Id,
                subscription.ChannelId,
                subscription.Channel.ChannelName,
                subscription.Channel.UsernameOrInviteLink,
                avatarImageUrl,
                subscription.IsActive ? "Active" : "Paused",
                subscription.IsActive,
                subscription.LastDeliveredAtUtc,
                subscription.Channel.LastCollectorError);
        }));

        return subscriptionDtos;
    }

    private static string BuildTrackedChannelReference(TrackedChannel channel)
    {
        var reference = channel.UsernameOrInviteLink?.Trim() ?? string.Empty;
        if (reference.StartsWith("https://t.me/", StringComparison.OrdinalIgnoreCase))
        {
            var slug = reference["https://t.me/".Length..].Trim('/');
            if (!string.IsNullOrWhiteSpace(slug) &&
                !slug.StartsWith("+", StringComparison.Ordinal) &&
                !slug.Contains('/'))
            {
                return $"@{slug.TrimStart('@')}";
            }
        }

        return reference;
    }
}
