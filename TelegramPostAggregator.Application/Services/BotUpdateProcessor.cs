using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Services.Bot;

namespace TelegramPostAggregator.Application.Services;

public sealed class BotUpdateProcessor(
    IUserService userService,
    IChannelTrackingService channelTrackingService,
    BotMenuFactory menuFactory,
    BotMessageCatalog messages) : IBotUpdateProcessor
{
    public async Task<BotCommandResultDto> ProcessAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken = default)
    {
        await userService.UpsertTelegramUserAsync(update.User, cancellationToken);

        if (!string.IsNullOrWhiteSpace(update.CallbackData))
        {
            return await ProcessCallbackAsync(update, cancellationToken);
        }

        var text = update.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new BotCommandResultDto(true, messages.EmptyUpdatePrompt, menuFactory.BuildMainMenu());
        }

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) || string.Equals(text, BotMenuFactory.StartLabel, StringComparison.OrdinalIgnoreCase))
        {
            var resumedCount = await channelTrackingService.SetSubscriptionsActiveAsync(update.User.TelegramUserId, true, cancellationToken);
            return new BotCommandResultDto(true, messages.BuildStartMessage(resumedCount), menuFactory.BuildMainMenu());
        }

        if (text.StartsWith("/stop", StringComparison.OrdinalIgnoreCase) || string.Equals(text, BotMenuFactory.StopLabel, StringComparison.OrdinalIgnoreCase))
        {
            return new BotCommandResultDto(
                true,
                messages.PauseConfirmationPrompt,
                menuFactory.BuildPauseConfirmationMenu(),
                messages.PauseConfirmationCallbackNotice);
        }

        if (text.StartsWith("/list", StringComparison.OrdinalIgnoreCase) || string.Equals(text, BotMenuFactory.ListLabel, StringComparison.OrdinalIgnoreCase))
        {
            return await BuildSubscriptionsListResultAsync(update.User.TelegramUserId, cancellationToken);
        }

        if (string.Equals(text, BotMenuFactory.DeleteAllLabel, StringComparison.OrdinalIgnoreCase))
        {
            return new BotCommandResultDto(
                true,
                messages.DeleteAllConfirmationPrompt,
                menuFactory.BuildDeleteAllConfirmationMenu(),
                messages.DeleteAllConfirmationCallbackNotice);
        }

        if (text.StartsWith("/remove", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return new BotCommandResultDto(false, messages.RemoveUsage, menuFactory.BuildMainMenu());
            }

            await channelTrackingService.RemoveTrackedChannelAsync(
                new RemoveTrackedChannelDto(update.User.TelegramUserId, parts[1]),
                cancellationToken);

            return new BotCommandResultDto(true, messages.BuildSubscriptionDisabledMessage(parts[1]), menuFactory.BuildMainMenu());
        }

        await channelTrackingService.AddTrackedChannelAsync(
            new AddTrackedChannelDto(update.User.TelegramUserId, update.User.TelegramUsername, update.User.DisplayName, text),
            cancellationToken);

        return new BotCommandResultDto(true, messages.BuildSubscriptionAddedMessage(text), menuFactory.BuildMainMenu());
    }

    private async Task<BotCommandResultDto> ProcessCallbackAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken)
    {
        var callbackData = update.CallbackData!;

        if (callbackData == "menu:list")
        {
            return await BuildSubscriptionsListResultAsync(update.User.TelegramUserId, cancellationToken);
        }

        if (callbackData == "menu:start")
        {
            var resumedCount = await channelTrackingService.SetSubscriptionsActiveAsync(update.User.TelegramUserId, true, cancellationToken);
            return new BotCommandResultDto(
                true,
                messages.BuildStartCallbackMessage(resumedCount),
                menuFactory.BuildMainMenu(),
                messages.StartCallbackNotice);
        }

        if (callbackData == "menu:stop")
        {
            return new BotCommandResultDto(
                true,
                messages.PauseConfirmationPrompt,
                menuFactory.BuildPauseConfirmationMenu(),
                messages.PauseConfirmationCallbackNotice);
        }

        if (callbackData == "menu:delete_all")
        {
            return new BotCommandResultDto(
                true,
                messages.DeleteAllConfirmationPrompt,
                menuFactory.BuildDeleteAllConfirmationMenu(),
                messages.DeleteAllConfirmationCallbackNotice);
        }

        if (callbackData == "pause_all:confirm")
        {
            var pausedCount = await channelTrackingService.SetSubscriptionsActiveAsync(update.User.TelegramUserId, false, cancellationToken);
            return new BotCommandResultDto(
                true,
                messages.BuildPauseAppliedMessage(pausedCount),
                menuFactory.BuildMainMenu(),
                messages.PauseAppliedNotice);
        }

        if (callbackData == "delete_all:confirm")
        {
            var removedCount = await channelTrackingService.RemoveAllTrackedChannelsAsync(update.User.TelegramUserId, cancellationToken);
            return new BotCommandResultDto(
                true,
                messages.BuildDeleteAllAppliedMessage(removedCount),
                menuFactory.BuildMainMenu(),
                messages.DeletionCompletedNotice);
        }

        if (callbackData == "action:cancel")
        {
            return new BotCommandResultDto(true, messages.ActionCancelledMessage, menuFactory.BuildMainMenu(), messages.ActionCancelledNotice);
        }

        if (callbackData.StartsWith("delete_one:confirm:", StringComparison.Ordinal))
        {
            if (!TryParseChannelId(callbackData["delete_one:confirm:".Length..], out var channelId))
            {
                return new BotCommandResultDto(false, messages.InvalidSubscriptionMessage, menuFactory.BuildMainMenu(), messages.ErrorNotice);
            }

            var removed = await channelTrackingService.RemoveTrackedChannelByIdAsync(
                new RemoveTrackedChannelByIdDto(update.User.TelegramUserId, channelId),
                cancellationToken);

            return new BotCommandResultDto(
                removed,
                removed ? messages.SubscriptionDeletedMessage : messages.SubscriptionNotFoundMessage,
                menuFactory.BuildMainMenu(),
                removed ? messages.SubscriptionDeletedNotice : messages.SubscriptionNotFoundNotice);
        }

        if (callbackData.StartsWith("delete_one:", StringComparison.Ordinal))
        {
            if (!TryParseChannelId(callbackData["delete_one:".Length..], out var channelId))
            {
                return new BotCommandResultDto(false, messages.InvalidSubscriptionMessage, menuFactory.BuildMainMenu(), messages.ErrorNotice);
            }

            var subscriptions = await channelTrackingService.ListSubscriptionsAsync(update.User.TelegramUserId, cancellationToken);
            var subscription = subscriptions.FirstOrDefault(x => x.ChannelId == channelId);
            if (subscription is null)
            {
                return new BotCommandResultDto(false, messages.SubscriptionNotFoundMessage, menuFactory.BuildMainMenu(), messages.SubscriptionNotFoundNotice);
            }

            return new BotCommandResultDto(
                true,
                messages.BuildDeleteOneConfirmationMessage(subscription),
                menuFactory.BuildDeleteOneConfirmationMenu(subscription.ChannelId),
                messages.DeleteOneConfirmationCallbackNotice);
        }

        return new BotCommandResultDto(false, messages.UnknownActionMessage, menuFactory.BuildMainMenu(), messages.UnknownActionNotice);
    }

    private async Task<BotCommandResultDto> BuildSubscriptionsListResultAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var subscriptions = await channelTrackingService.ListSubscriptionsAsync(telegramUserId, cancellationToken);
        if (subscriptions.Count == 0)
        {
            return new BotCommandResultDto(true, messages.EmptySubscriptionsMessage, menuFactory.BuildMainMenu());
        }

        return new BotCommandResultDto(
            true,
            messages.BuildSubscriptionsListMessage(subscriptions),
            menuFactory.BuildSubscriptionsMenu(subscriptions),
            messages.SubscriptionsListUpdatedNotice);
    }

    private static bool TryParseChannelId(string value, out Guid channelId) =>
        Guid.TryParse(value, out channelId);
}
