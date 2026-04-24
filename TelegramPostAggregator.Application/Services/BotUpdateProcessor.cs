using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Services.Bot;

namespace TelegramPostAggregator.Application.Services;

public sealed class BotUpdateProcessor(
    IUserService userService,
    IChannelTrackingService channelTrackingService,
    BotLocalizationCatalog localizationCatalog,
    BotMenuFactory menuFactory,
    BotMessageCatalog messages) : IBotUpdateProcessor
{
    public async Task<BotCommandResultDto> ProcessAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken = default)
    {
        var user = await userService.UpsertTelegramUserAsync(update.User, cancellationToken);
        var languageCode = localizationCatalog.NormalizeLanguageCode(user.PreferredLanguageCode);

        if (!string.IsNullOrWhiteSpace(update.CallbackData))
        {
            return await ProcessCallbackAsync(update, languageCode, cancellationToken);
        }

        var text = update.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new BotCommandResultDto(true, messages.EmptyUpdatePrompt(languageCode), menuFactory.BuildMainMenu(languageCode));
        }

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) ||
            (localizationCatalog.TryResolveMainMenuAction(text, out var menuAction) && menuAction == BotMainMenuAction.Start))
        {
            var resumedCount = await channelTrackingService.SetSubscriptionsActiveAsync(update.User.TelegramUserId, true, cancellationToken);
            return new BotCommandResultDto(true, messages.BuildStartMessage(resumedCount, languageCode), menuFactory.BuildMainMenu(languageCode));
        }

        if (text.StartsWith("/stop", StringComparison.OrdinalIgnoreCase) ||
            (localizationCatalog.TryResolveMainMenuAction(text, out menuAction) && menuAction == BotMainMenuAction.Stop))
        {
            return new BotCommandResultDto(
                true,
                messages.PauseConfirmationPrompt(languageCode),
                menuFactory.BuildPauseConfirmationMenu(languageCode),
                messages.PauseConfirmationCallbackNotice(languageCode));
        }

        if (text.StartsWith("/list", StringComparison.OrdinalIgnoreCase) ||
            (localizationCatalog.TryResolveMainMenuAction(text, out menuAction) && menuAction == BotMainMenuAction.List))
        {
            return await BuildSubscriptionsListResultAsync(update.User.TelegramUserId, languageCode, cancellationToken);
        }

        if (localizationCatalog.TryResolveMainMenuAction(text, out menuAction) && menuAction == BotMainMenuAction.DeleteAll)
        {
            return new BotCommandResultDto(
                true,
                messages.DeleteAllConfirmationPrompt(languageCode),
                menuFactory.BuildDeleteAllConfirmationMenu(languageCode),
                messages.DeleteAllConfirmationCallbackNotice(languageCode));
        }

        if (localizationCatalog.TryResolveMainMenuAction(text, out menuAction) && menuAction == BotMainMenuAction.Language)
        {
            return new BotCommandResultDto(
                true,
                messages.LanguageSelectionPrompt(languageCode),
                menuFactory.BuildLanguageMenu(languageCode),
                messages.StartCallbackNotice(languageCode));
        }

        if (localizationCatalog.TryResolveLanguageSelection(text, out var selectedLanguageCode))
        {
            var updatedUser = await userService.SetPreferredLanguageAsync(update.User.TelegramUserId, selectedLanguageCode, cancellationToken);
            var updatedLanguageCode = localizationCatalog.NormalizeLanguageCode(updatedUser.PreferredLanguageCode);

            return new BotCommandResultDto(
                true,
                messages.BuildLanguageUpdatedMessage(selectedLanguageCode, updatedLanguageCode),
                menuFactory.BuildMainMenu(updatedLanguageCode),
                messages.StartCallbackNotice(updatedLanguageCode));
        }

        if (localizationCatalog.IsReservedUiText(text))
        {
            return new BotCommandResultDto(
                true,
                messages.EmptyUpdatePrompt(languageCode),
                menuFactory.BuildMainMenu(languageCode));
        }

        if (text.StartsWith("/remove", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return new BotCommandResultDto(false, messages.RemoveUsage(languageCode), menuFactory.BuildMainMenu(languageCode));
            }

            await channelTrackingService.RemoveTrackedChannelAsync(
                new RemoveTrackedChannelDto(update.User.TelegramUserId, parts[1]),
                cancellationToken);

            return new BotCommandResultDto(true, messages.BuildSubscriptionDisabledMessage(parts[1], languageCode), menuFactory.BuildMainMenu(languageCode));
        }

        await channelTrackingService.AddTrackedChannelAsync(
            new AddTrackedChannelDto(update.User.TelegramUserId, update.User.TelegramUsername, update.User.DisplayName, text),
            cancellationToken);

        return new BotCommandResultDto(true, messages.BuildSubscriptionAddedMessage(text, languageCode), menuFactory.BuildMainMenu(languageCode));
    }

    private async Task<BotCommandResultDto> ProcessCallbackAsync(TelegramBotUpdateDto update, string languageCode, CancellationToken cancellationToken)
    {
        var callbackData = update.CallbackData!;

        if (callbackData == "menu:list")
        {
            return await BuildSubscriptionsListResultAsync(update.User.TelegramUserId, languageCode, cancellationToken);
        }

        if (callbackData == "menu:start")
        {
            var resumedCount = await channelTrackingService.SetSubscriptionsActiveAsync(update.User.TelegramUserId, true, cancellationToken);
            return new BotCommandResultDto(
                true,
                messages.BuildStartCallbackMessage(resumedCount, languageCode),
                menuFactory.BuildMainMenu(languageCode),
                messages.StartCallbackNotice(languageCode));
        }

        if (callbackData == "menu:stop")
        {
            return new BotCommandResultDto(
                true,
                messages.PauseConfirmationPrompt(languageCode),
                menuFactory.BuildPauseConfirmationMenu(languageCode),
                messages.PauseConfirmationCallbackNotice(languageCode));
        }

        if (callbackData == "menu:delete_all")
        {
            return new BotCommandResultDto(
                true,
                messages.DeleteAllConfirmationPrompt(languageCode),
                menuFactory.BuildDeleteAllConfirmationMenu(languageCode),
                messages.DeleteAllConfirmationCallbackNotice(languageCode));
        }

        if (callbackData == "menu:language")
        {
            return new BotCommandResultDto(
                true,
                messages.LanguageSelectionPrompt(languageCode),
                menuFactory.BuildLanguageMenu(languageCode),
                messages.StartCallbackNotice(languageCode));
        }

        if (callbackData.StartsWith("language:set:", StringComparison.Ordinal))
        {
            var selectedLanguageCode = localizationCatalog.NormalizeLanguageCode(callbackData["language:set:".Length..]);
            var updatedUser = await userService.SetPreferredLanguageAsync(update.User.TelegramUserId, selectedLanguageCode, cancellationToken);
            var updatedLanguageCode = localizationCatalog.NormalizeLanguageCode(updatedUser.PreferredLanguageCode);

            return new BotCommandResultDto(
                true,
                messages.BuildLanguageUpdatedMessage(selectedLanguageCode, updatedLanguageCode),
                menuFactory.BuildMainMenu(updatedLanguageCode),
                messages.StartCallbackNotice(updatedLanguageCode));
        }

        if (callbackData == "pause_all:confirm")
        {
            var pausedCount = await channelTrackingService.SetSubscriptionsActiveAsync(update.User.TelegramUserId, false, cancellationToken);
            return new BotCommandResultDto(
                true,
                messages.BuildPauseAppliedMessage(pausedCount, languageCode),
                menuFactory.BuildMainMenu(languageCode),
                messages.PauseAppliedNotice(languageCode));
        }

        if (callbackData == "delete_all:confirm")
        {
            var removedCount = await channelTrackingService.RemoveAllTrackedChannelsAsync(update.User.TelegramUserId, cancellationToken);
            return new BotCommandResultDto(
                true,
                messages.BuildDeleteAllAppliedMessage(removedCount, languageCode),
                menuFactory.BuildMainMenu(languageCode),
                messages.DeletionCompletedNotice(languageCode));
        }

        if (callbackData == "action:cancel")
        {
            return new BotCommandResultDto(true, messages.ActionCancelledMessage(languageCode), menuFactory.BuildMainMenu(languageCode), messages.ActionCancelledNotice(languageCode));
        }

        if (callbackData.StartsWith("delete_one:confirm:", StringComparison.Ordinal))
        {
            if (!TryParseChannelId(callbackData["delete_one:confirm:".Length..], out var channelId))
            {
                return new BotCommandResultDto(false, messages.InvalidSubscriptionMessage(languageCode), menuFactory.BuildMainMenu(languageCode), messages.ErrorNotice(languageCode));
            }

            var removed = await channelTrackingService.RemoveTrackedChannelByIdAsync(
                new RemoveTrackedChannelByIdDto(update.User.TelegramUserId, channelId),
                cancellationToken);

            return new BotCommandResultDto(
                removed,
                removed ? messages.SubscriptionDeletedMessage(languageCode) : messages.SubscriptionNotFoundMessage(languageCode),
                menuFactory.BuildMainMenu(languageCode),
                removed ? messages.SubscriptionDeletedNotice(languageCode) : messages.SubscriptionNotFoundNotice(languageCode));
        }

        if (callbackData.StartsWith("delete_one:", StringComparison.Ordinal))
        {
            if (!TryParseChannelId(callbackData["delete_one:".Length..], out var channelId))
            {
                return new BotCommandResultDto(false, messages.InvalidSubscriptionMessage(languageCode), menuFactory.BuildMainMenu(languageCode), messages.ErrorNotice(languageCode));
            }

            var subscriptions = await channelTrackingService.ListSubscriptionsAsync(update.User.TelegramUserId, cancellationToken);
            var subscription = subscriptions.FirstOrDefault(x => x.ChannelId == channelId);
            if (subscription is null)
            {
                return new BotCommandResultDto(false, messages.SubscriptionNotFoundMessage(languageCode), menuFactory.BuildMainMenu(languageCode), messages.SubscriptionNotFoundNotice(languageCode));
            }

            return new BotCommandResultDto(
                true,
                messages.BuildDeleteOneConfirmationMessage(subscription, languageCode),
                menuFactory.BuildDeleteOneConfirmationMenu(subscription.ChannelId, languageCode),
                messages.DeleteOneConfirmationCallbackNotice(languageCode));
        }

        return new BotCommandResultDto(false, messages.UnknownActionMessage(languageCode), menuFactory.BuildMainMenu(languageCode), messages.UnknownActionNotice(languageCode));
    }

    private async Task<BotCommandResultDto> BuildSubscriptionsListResultAsync(long telegramUserId, string languageCode, CancellationToken cancellationToken)
    {
        var subscriptions = await channelTrackingService.ListSubscriptionsAsync(telegramUserId, cancellationToken);
        if (subscriptions.Count == 0)
        {
            return new BotCommandResultDto(true, messages.EmptySubscriptionsMessage(languageCode), menuFactory.BuildMainMenu(languageCode));
        }

        return new BotCommandResultDto(
            true,
            messages.BuildSubscriptionsListMessage(subscriptions, languageCode),
            menuFactory.BuildSubscriptionsMenu(subscriptions, languageCode),
            messages.SubscriptionsListUpdatedNotice(languageCode));
    }

    private static bool TryParseChannelId(string value, out Guid channelId) =>
        Guid.TryParse(value, out channelId);
}
