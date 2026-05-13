using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Collections.Concurrent;
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
    IBillingService billingService,
    IPostRepository postRepository,
    ITelegramBotGateway telegramBotGateway,
    IServiceScopeFactory scopeFactory,
    IErrorAlertService errorAlertService,
    Microsoft.Extensions.Logging.ILogger<MiniAppChannelService> logger) : IMiniAppChannelService
{
    private static readonly TimeSpan SharedChannelRequestCooldown = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> SharedChannelRequestCooldowns = new();

    public async Task<IReadOnlyList<MiniAppChannelDto>> ListAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var channels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        if (channels.Count == 0)
        {
            return [];
        }

        channels = await ReconcileManagedChannelsAsync(channels, cancellationToken);
        if (channels.Count == 0)
        {
            return [];
        }

        channels = await EnforceManagedChannelActivityLimitAsync(telegramUserId, channels, cancellationToken);
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

        var visibleChannels = channels
            .OrderByDescending(channel => channel.IsActive)
            .ThenBy(channel => channel.ChannelName)
            .ToList();

        var channelDtos = await Task.WhenAll(visibleChannels.Select(channel =>
            BuildChannelDtoAsync(channel, subscriptionsByManagedChannelId, cancellationToken)));

        return channelDtos;
    }

    private async Task<IReadOnlyList<ManagedChannel>> ReconcileManagedChannelsAsync(
        IReadOnlyList<ManagedChannel> channels,
        CancellationToken cancellationToken)
    {
        var visibleChannels = new List<ManagedChannel>(channels.Count);
        var changed = false;

        foreach (var channel in channels)
        {
            var stillAdministrator = await telegramBotGateway.IsBotAdministratorAsync(
                channel.TelegramChatId.ToString(),
                cancellationToken);

            if (stillAdministrator)
            {
                if (IsAdminAccessError(channel.LastWriteError))
                {
                    channel.LastWriteError = null;
                    channel.LastVerifiedAtUtc = DateTimeOffset.UtcNow;
                    changed = true;
                }

                visibleChannels.Add(channel);
                continue;
            }

            logger.LogInformation(
                "Hiding managed channel because bot is no longer administrator. ManagedChannelId={ManagedChannelId}, ChatId={ChatId}",
                channel.Id,
                channel.TelegramChatId);

            channel.IsActive = false;
            channel.LastWriteError = "Bot is no longer an administrator in this channel.";
            channel.LastVerifiedAtUtc = DateTimeOffset.UtcNow;
            changed = true;
        }

        if (changed)
        {
            await managedChannelRepository.SaveChangesAsync(cancellationToken);
        }

        return visibleChannels;
    }

    public async Task<ManagedChannelRegistrationResultDto> RegisterSharedChannelAsync(long telegramUserId, TelegramSharedChatDto sharedChat, CancellationToken cancellationToken = default)
    {
        var dedupeKey = $"shared-channel:{telegramUserId}:{sharedChat.RequestId}:{sharedChat.ChatId}";
        var now = DateTimeOffset.UtcNow;
        if (SharedChannelRequestCooldowns.TryGetValue(dedupeKey, out var lastSeenAtUtc) &&
            now - lastSeenAtUtc < SharedChannelRequestCooldown)
        {
            logger.LogInformation(
                "Skipping duplicate shared channel registration request. TelegramUserId={TelegramUserId}, RequestId={RequestId}, SharedChatId={SharedChatId}",
                telegramUserId,
                sharedChat.RequestId,
                sharedChat.ChatId);
            return new ManagedChannelRegistrationResultDto(true, "Channel request is already being processed.");
        }

        SharedChannelRequestCooldowns[dedupeKey] = now;

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
            await errorAlertService.SendAsync(
                "Managed channel registration aborted",
                $"TelegramUserId: {telegramUserId}\nSharedChatId: {sharedChat.ChatId}\nReason: app user not found",
                cancellationToken: cancellationToken);
            return new ManagedChannelRegistrationResultDto(false, "Start the bot first, then add your channel again.");
        }

        var managedChannelQuota = await EnsureManagedChannelQuotaAsync(telegramUserId, sharedChat.ChatId, cancellationToken);
        if (managedChannelQuota is not null)
        {
            return new ManagedChannelRegistrationResultDto(false, managedChannelQuota.Message);
        }

        var existing = await managedChannelRepository.GetByTelegramChatIdAsync(user.Id, sharedChat.ChatId, cancellationToken);
        var managedChannel = existing ?? new ManagedChannel
        {
            UserId = user.Id,
            TelegramChatId = sharedChat.ChatId
        };
        var shouldActivate = existing?.IsActive ?? await CanActivateManagedChannelAsync(telegramUserId, existing?.Id, cancellationToken);

        managedChannel.ChannelName = !string.IsNullOrWhiteSpace(sharedChat.Title)
            ? sharedChat.Title.Trim()
            : BuildFallbackChannelName(sharedChat);
        managedChannel.Username = NormalizeUsername(sharedChat.Username);
        managedChannel.IsActive = shouldActivate;
        managedChannel.LastVerifiedAtUtc = DateTimeOffset.UtcNow;
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

        if (shouldActivate)
        {
            _ = RunProbeInBackgroundAsync(managedChannel.Id, telegramUserId, sharedChat.ChatId);
        }

        return new ManagedChannelRegistrationResultDto(
            true,
            shouldActivate
                ? $"Channel added: {managedChannel.ChannelName}. A verification message will appear shortly."
                : $"Channel added: {managedChannel.ChannelName}. All active slots are already in use, so this channel was added in paused mode. Pause another active channel to activate this one.");

#pragma warning disable CS0162
        var registrationMessage = shouldActivate
            ? $"Канал додано: {managedChannel.ChannelName}. Перевірочне повідомлення з’явиться трохи пізніше."
            : $"Канал додано: {managedChannel.ChannelName}. Усі активні слоти вже зайняті, тому цей канал додано на паузі. Щоб активувати його, поставте інший активний канал на паузу.";

        return new ManagedChannelRegistrationResultDto(true, registrationMessage);

        return new ManagedChannelRegistrationResultDto(
            true,
            $"Канал додано: {managedChannel.ChannelName}. Якщо це щойно створений канал, додайте бота адміністратором і ще раз натисніть «Додати мій канал / додати адміна». Перевірочне повідомлення з’явиться трохи пізніше.");
    }

#pragma warning restore CS0162
    public async Task<bool> SyncBotMembershipAsync(long telegramUserId, TelegramMyChatMemberDto membership, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(membership.ChatType, "channel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var user = await appUserRepository.GetByTelegramUserIdAsync(telegramUserId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        var normalizedStatus = membership.NewStatus?.Trim().ToLowerInvariant();
        var isAdminStatus = normalizedStatus is "administrator" or "creator";
        var isRemovedStatus = normalizedStatus is "left" or "kicked";
        if (!isAdminStatus && !isRemovedStatus)
        {
            return false;
        }

        var managedChannel = await managedChannelRepository.GetByTelegramChatIdAsync(user.Id, membership.ChatId, cancellationToken);
        if (managedChannel is null && !isAdminStatus)
        {
            return false;
        }

        if (managedChannel is null && isAdminStatus)
        {
            var managedChannelQuota = await EnsureManagedChannelQuotaAsync(telegramUserId, membership.ChatId, cancellationToken);
            if (managedChannelQuota is not null)
            {
                logger.LogInformation(
                    "Skipping managed channel sync because owned channel limit was reached. TelegramUserId={TelegramUserId}, ChatId={ChatId}",
                    telegramUserId,
                    membership.ChatId);

                await telegramBotGateway.SendMessageAsync(
                    new TelegramBotOutboundMessageDto(
                        telegramUserId,
                        managedChannelQuota.Message,
                        ParseMode: null,
                        DisableWebPagePreview: true),
                    cancellationToken);

                return false;
            }
        }

        if (managedChannel is null)
        {
            managedChannel = new ManagedChannel
            {
                UserId = user.Id,
                TelegramChatId = membership.ChatId,
                IsActive = false
            };

            await managedChannelRepository.AddAsync(managedChannel, cancellationToken);
        }

        managedChannel.ChannelName = !string.IsNullOrWhiteSpace(membership.Title)
            ? membership.Title.Trim()
            : managedChannel.ChannelName;
        managedChannel.Username = NormalizeUsername(membership.Username) ?? managedChannel.Username;
        managedChannel.LastVerifiedAtUtc = DateTimeOffset.UtcNow;

        if (isAdminStatus)
        {
            managedChannel.IsActive = managedChannel.IsActive ||
                                      (IsAdminAccessError(managedChannel.LastWriteError) &&
                                       await CanActivateManagedChannelAsync(telegramUserId, managedChannel.Id, cancellationToken));
            managedChannel.LastWriteError = null;
        }
        else
        {
            managedChannel.IsActive = false;
            managedChannel.LastWriteError = "Bot no longer has administrator access to this channel.";
        }

        await managedChannelRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Synced bot membership for managed channel. TelegramUserId={TelegramUserId}, ChatId={ChatId}, NewStatus={NewStatus}, ManagedChannelId={ManagedChannelId}",
            telegramUserId,
            membership.ChatId,
            membership.NewStatus,
            managedChannel.Id);

        return true;
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

        if (isActive)
        {
            var canActivate = await CanActivateManagedChannelAsync(telegramUserId, target.Id, cancellationToken);
            if (!canActivate)
            {
                return false;
            }
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

        var leaveResult = await telegramBotGateway.LeaveChatAsync(target.TelegramChatId.ToString(), cancellationToken);
        if (!leaveResult.IsSuccessStatusCode && !CanDeleteAfterLeaveFailure(leaveResult.ResponseBody))
        {
            logger.LogWarning(
                "Failed to remove bot from managed channel before deletion. TelegramUserId={TelegramUserId}, ManagedChannelId={ManagedChannelId}, ChatId={ChatId}, Response={Response}",
                telegramUserId,
                target.Id,
                target.TelegramChatId,
                leaveResult.ResponseBody);

            await errorAlertService.SendAsync(
                "Managed channel delete failed",
                $"TelegramUserId: {telegramUserId}\nManagedChannelId: {target.Id}\nChatId: {target.TelegramChatId}\nResponse: {leaveResult.ResponseBody}",
                cancellationToken: cancellationToken);

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

    private async Task<ChannelTrackingResultDto?> EnsureManagedChannelQuotaAsync(
        long telegramUserId,
        long telegramChatId,
        CancellationToken cancellationToken)
    {
        var decision = await billingService.CanAddManagedChannelAsync(telegramUserId, telegramChatId, cancellationToken);
        return decision.Success ? null : decision;
    }

    private async Task<bool> CanActivateManagedChannelAsync(
        long telegramUserId,
        Guid? excludedManagedChannelId,
        CancellationToken cancellationToken)
    {
        var usage = await billingService.GetSubscriptionUsageAsync(telegramUserId, cancellationToken);
        var channels = await managedChannelRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var activeChannelsCount = channels.Count(channel => channel.IsActive && channel.Id != excludedManagedChannelId);
        return activeChannelsCount < usage.ManagedChannelLimit;
    }

    private async Task<IReadOnlyList<ManagedChannel>> EnforceManagedChannelActivityLimitAsync(
        long telegramUserId,
        IReadOnlyList<ManagedChannel> channels,
        CancellationToken cancellationToken)
    {
        var usage = await billingService.GetSubscriptionUsageAsync(telegramUserId, cancellationToken);
        var activeChannels = channels.Where(channel => channel.IsActive).ToList();
        if (activeChannels.Count <= usage.ManagedChannelLimit)
        {
            return channels;
        }

        var now = DateTimeOffset.UtcNow;
        var overflowChannels = activeChannels
            .Skip(Math.Max(usage.ManagedChannelLimit, 0))
            .ToList();
        var overflowIds = overflowChannels
            .Select(channel => channel.Id)
            .ToHashSet();

        foreach (var overflowChannel in overflowChannels)
        {
            overflowChannel.IsActive = false;
            overflowChannel.UpdatedAtUtc = now;
        }

        var subscriptions = await managedChannelSubscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        foreach (var subscription in subscriptions.Where(x => overflowIds.Contains(x.ManagedChannelId) && x.IsActive))
        {
            subscription.IsActive = false;
            subscription.UpdatedAtUtc = now;
        }

        await managedChannelSubscriptionRepository.SaveChangesAsync(cancellationToken);
        await managedChannelRepository.SaveChangesAsync(cancellationToken);

        return channels;
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

    private Task<TelegramBotApiResultDto> SendProbeMessageAsync(long chatId, CancellationToken cancellationToken) =>
        telegramBotGateway.SendMessageAsync(
            new TelegramBotOutboundMessageDto(
                chatId,
                "Channels Monitor connected successfully.",
                ParseMode: null,
                DisableWebPagePreview: true),
            cancellationToken);

    private static bool IsPermanentWriteFailure(TelegramBotApiResultDto result) =>
        result.StatusCode != HttpStatusCode.TooManyRequests;

    private static bool IsAdminRightsIssue(string? responseBody) =>
        !string.IsNullOrWhiteSpace(responseBody) &&
        (responseBody.Contains("bot was kicked", StringComparison.OrdinalIgnoreCase) ||
         responseBody.Contains("not enough rights", StringComparison.OrdinalIgnoreCase) ||
         responseBody.Contains("administrator", StringComparison.OrdinalIgnoreCase) ||
         responseBody.Contains("forbidden", StringComparison.OrdinalIgnoreCase));

    private static bool IsAdminAccessError(string? responseBody) =>
        !string.IsNullOrWhiteSpace(responseBody) &&
        responseBody.Contains("administrator", StringComparison.OrdinalIgnoreCase);

    private static bool CanDeleteAfterLeaveFailure(string? responseBody) =>
        !string.IsNullOrWhiteSpace(responseBody) &&
        (responseBody.Contains("bot is not a member", StringComparison.OrdinalIgnoreCase) ||
         responseBody.Contains("chat not found", StringComparison.OrdinalIgnoreCase) ||
         responseBody.Contains("bot was kicked", StringComparison.OrdinalIgnoreCase));

    private async Task RunProbeInBackgroundAsync(Guid managedChannelId, long telegramUserId, long sharedChatId)
    {
        try
        {
            var probeResult = await SendProbeMessageAsync(sharedChatId, CancellationToken.None);

            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IManagedChannelRepository>();
            var managedChannel = await repository.GetByIdAsync(managedChannelId, CancellationToken.None);
            if (managedChannel is null)
            {
                return;
            }

            managedChannel.LastVerifiedAtUtc = DateTimeOffset.UtcNow;
            if (probeResult.IsSuccessStatusCode)
            {
                managedChannel.LastWriteSucceededAtUtc = DateTimeOffset.UtcNow;
                managedChannel.LastWriteError = null;
                managedChannel.IsActive = true;
            }
            else
            {
                managedChannel.LastWriteError = probeResult.ResponseBody ?? "The bot could not post to this channel.";
                if (IsPermanentWriteFailure(probeResult))
                {
                    managedChannel.IsActive = false;
                }

                logger.LogWarning(
                    "Shared channel background probe failed. TelegramUserId={TelegramUserId}, SharedChatId={SharedChatId}, Response={ResponseBody}",
                    telegramUserId,
                    sharedChatId,
                    probeResult.ResponseBody);
                if (false && IsAdminRightsIssue(probeResult.ResponseBody))
                {
                    await telegramBotGateway.SendMessageAsync(
                        new TelegramBotOutboundMessageDto(
                            telegramUserId,
                            "Telegram повідомив, що бот ще не є адміністратором цього каналу. Додайте бота адміністратором і спробуйте ще раз.",
                            ParseMode: null,
                            DisableWebPagePreview: true),
                        CancellationToken.None);
                }
                if (false && IsAdminRightsIssue(probeResult.ResponseBody))
                {
                    await telegramBotGateway.SendMessageAsync(
                        new TelegramBotOutboundMessageDto(
                            telegramUserId,
                            "Telegram повідомив, що бот ще не є адміністратором цього каналу. Додайте бота адміністратором і ще раз натисніть «Додати мій канал / додати адміна».",
                            ParseMode: null,
                            DisableWebPagePreview: true),
                        CancellationToken.None);
                }

                if (IsAdminRightsIssue(probeResult.ResponseBody))
                {
                    await telegramBotGateway.SendMessageAsync(
                        new TelegramBotOutboundMessageDto(
                            telegramUserId,
                            "Telegram reported that the bot is not yet an administrator in this channel. Add the bot as an administrator and try again.",
                            ParseMode: null,
                            DisableWebPagePreview: true),
                        CancellationToken.None);
                }

                await errorAlertService.SendAsync(
                    "Managed channel verification failed",
                    $"TelegramUserId: {telegramUserId}\nSharedChatId: {sharedChatId}\nResponse: {probeResult.ResponseBody}",
                    cancellationToken: CancellationToken.None);
            }

            await repository.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Shared channel background probe failed unexpectedly. TelegramUserId={TelegramUserId}, SharedChatId={SharedChatId}",
                telegramUserId,
                sharedChatId);
            await errorAlertService.SendAsync(
                "Managed channel verification crashed",
                $"TelegramUserId: {telegramUserId}\nSharedChatId: {sharedChatId}",
                exception,
                CancellationToken.None);
        }
    }

}
