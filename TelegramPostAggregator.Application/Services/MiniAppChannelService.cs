using Microsoft.Extensions.Logging;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Services;

public sealed class MiniAppChannelService(
    IManagedChannelRepository managedChannelRepository,
    IAppUserRepository appUserRepository,
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

        var adminChecks = await Task.WhenAll(channels.Select(async channel => new
        {
            Channel = channel,
            IsBotAdministrator = await telegramBotGateway.IsBotAdministratorAsync(channel.TelegramChatId.ToString(), cancellationToken)
        }));

        return adminChecks
            .Where(result => result.IsBotAdministrator)
            .Select(result => result.Channel)
            .OrderByDescending(channel => channel.IsActive)
            .ThenBy(channel => channel.ChannelName)
            .Select(channel => new MiniAppChannelDto(
                channel.Id,
                channel.ChannelName,
                BuildChannelReference(channel),
                channel.IsActive ? "Connected" : "Paused",
                channel.IsActive,
                channel.LastWriteSucceededAtUtc ?? channel.LastVerifiedAtUtc,
                channel.LastWriteError))
            .ToList();
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
}
