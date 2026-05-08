using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Services.Bot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace TelegramPostAggregator.Application.Services;

public sealed class BotUpdateProcessor(
    IUserService userService,
    IChannelTrackingService channelTrackingService,
    IBillingService billingService,
    IMiniAppChannelService miniAppChannelService,
    ITelegramBotGateway telegramBotGateway,
    IServiceScopeFactory scopeFactory,
    BotLocalizationCatalog localizationCatalog,
    BotMenuFactory menuFactory,
    BotMessageCatalog messages,
    ILogger<BotUpdateProcessor> logger) : IBotUpdateProcessor
{
    private static readonly Regex ManagedChannelReferenceRegex = new(
        @"^\s*(?:https?://)?t\.me/[A-Za-z0-9_+/-]+(?:[/?#][^\s]*)?\s*['"",.;:!?)}\]]*\s*$|^\s*@[A-Za-z0-9_]{4,}\s*['"",.;:!?)}\]]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly TimeSpan SharedChatFollowupWindow = TimeSpan.FromSeconds(15);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, (DateTimeOffset RegisteredAtUtc, string? ChannelTitle)> RecentSharedChats = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> RecentSharedChannelUpdates = new();

    public async Task<BotCommandResultDto> ProcessAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken = default)
    {
        if (update.IsChannelPost)
        {
            return await ProcessManagedChannelPostAsync(update, cancellationToken);
        }

        if (update.PreCheckoutQuery is not null)
        {
            var decision = await billingService.ValidatePreCheckoutAsync(update.PreCheckoutQuery, cancellationToken);
            return new BotCommandResultDto(
                decision.IsApproved,
                string.Empty,
                PreCheckoutResponse: new TelegramPreCheckoutResponseDto(
                    update.PreCheckoutQuery.Id,
                    decision.IsApproved,
                    decision.ErrorMessage));
        }

        if (update.User is null)
        {
            return new BotCommandResultDto(false, string.Empty);
        }

        var sourceUser = update.User;
        var user = await userService.UpsertTelegramUserAsync(sourceUser, cancellationToken);
        var languageCode = localizationCatalog.NormalizeLanguageCode(user.PreferredLanguageCode);

        if (update.SuccessfulPayment is not null)
        {
            var paymentResult = await billingService.ProcessSuccessfulPaymentAsync(sourceUser.TelegramUserId, update.SuccessfulPayment, cancellationToken);
            return new BotCommandResultDto(paymentResult.Success, paymentResult.Message, menuFactory.BuildMainMenu(languageCode));
        }

        if (update.MyChatMember is not null)
        {
            await miniAppChannelService.SyncBotMembershipAsync(sourceUser.TelegramUserId, update.MyChatMember, cancellationToken);
            return new BotCommandResultDto(true, string.Empty, menuFactory.BuildMainMenu(languageCode));
        }

        if (!string.IsNullOrWhiteSpace(update.CallbackData))
        {
            return await ProcessCallbackAsync(update, languageCode, cancellationToken);
        }

        if (update.SharedChat is not null)
        {
            var sharedChatKey = $"{sourceUser.TelegramUserId}:{update.SharedChat.ChatId}";
            var now = DateTimeOffset.UtcNow;
            if (RecentSharedChannelUpdates.TryGetValue(sharedChatKey, out var lastSeenAtUtc) &&
                now - lastSeenAtUtc < TimeSpan.FromSeconds(30))
            {
                return new BotCommandResultDto(true, string.Empty, menuFactory.BuildMainMenu(languageCode));
            }

            RecentSharedChannelUpdates[sharedChatKey] = now;
            RecentSharedChats[sourceUser.TelegramUserId] = (DateTimeOffset.UtcNow, update.SharedChat.Title?.Trim());
            _ = Task.Run(
                () => RegisterSharedChannelInBackgroundAsync(sourceUser.TelegramUserId, update.SharedChat),
                CancellationToken.None);

            return new BotCommandResultDto(
                true,
                "Запит прийнято. Обробляю канал у фоновому режимі. Якщо канал щойно створено, додайте бота адміністратором і ще раз натисніть «Додати мій канал / додати адміна».",
                menuFactory.BuildMainMenu(languageCode));
        }

        var text = update.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new BotCommandResultDto(true, messages.EmptyUpdatePrompt(languageCode), menuFactory.BuildMainMenu(languageCode));
        }

        if (ShouldIgnoreSharedChatFollowup(sourceUser.TelegramUserId, text))
        {
            return new BotCommandResultDto(
                true,
                messages.ManagedChannelsPrompt(),
                menuFactory.BuildMainMenu(languageCode));
        }

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) ||
            (localizationCatalog.TryResolveMainMenuAction(text, out var menuAction) && menuAction == BotMainMenuAction.Start))
        {
            var resumedCount = await channelTrackingService.SetSubscriptionsActiveAsync(sourceUser.TelegramUserId, true, cancellationToken);
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
            return await BuildSubscriptionsListResultAsync(sourceUser.TelegramUserId, languageCode, cancellationToken);
        }

        if (text.StartsWith("/faq", StringComparison.OrdinalIgnoreCase) ||
            (localizationCatalog.TryResolveMainMenuAction(text, out menuAction) && menuAction == BotMainMenuAction.Faq))
        {
            return new BotCommandResultDto(true, messages.BuildBotFaqMessage(languageCode), menuFactory.BuildMainMenu(languageCode));
        }

        if (text.StartsWith("/plans", StringComparison.OrdinalIgnoreCase) ||
            (localizationCatalog.TryResolveMainMenuAction(text, out menuAction) && menuAction == BotMainMenuAction.Plans))
        {
            return await BuildPlansResultAsync(sourceUser, languageCode, cancellationToken);
        }

        if (text.StartsWith("/support", StringComparison.OrdinalIgnoreCase) ||
            (localizationCatalog.TryResolveMainMenuAction(text, out menuAction) && menuAction == BotMainMenuAction.Support))
        {
            return await BuildSupportResultAsync(languageCode, cancellationToken);
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
            var updatedUser = await userService.SetPreferredLanguageAsync(sourceUser.TelegramUserId, selectedLanguageCode, cancellationToken);
            var updatedLanguageCode = localizationCatalog.NormalizeLanguageCode(updatedUser.PreferredLanguageCode);

            return new BotCommandResultDto(
                true,
                messages.BuildLanguageUpdatedMessage(selectedLanguageCode, updatedLanguageCode),
                menuFactory.BuildMainMenu(updatedLanguageCode),
                messages.StartCallbackNotice(updatedLanguageCode));
        }

        if (localizationCatalog.IsManagedChannelsRequestLabel(text))
        {
            return new BotCommandResultDto(
                true,
                messages.ManagedChannelsPrompt(),
                menuFactory.BuildMainMenu(languageCode));
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
                new RemoveTrackedChannelDto(sourceUser.TelegramUserId, parts[1]),
                cancellationToken);

            return new BotCommandResultDto(true, messages.BuildSubscriptionDisabledMessage(parts[1], languageCode), menuFactory.BuildMainMenu(languageCode));
        }

        var addResult = await channelTrackingService.AddTrackedChannelAsync(
            new AddTrackedChannelDto(sourceUser.TelegramUserId, sourceUser.TelegramUsername, sourceUser.DisplayName, text),
            cancellationToken);

        var addMessage = addResult.Success
            ? messages.BuildSubscriptionAddedMessage(text, languageCode)
            : addResult.Message;

        return new BotCommandResultDto(addResult.Success, addMessage, menuFactory.BuildMainMenu(languageCode));
    }

    private async Task<BotCommandResultDto> ProcessManagedChannelPostAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken)
    {
        var chatId = update.ChatId;
        var text = update.Text?.Trim();
        if (!chatId.HasValue || string.IsNullOrWhiteSpace(text) || !ManagedChannelReferenceRegex.IsMatch(text))
        {
            return new BotCommandResultDto(true, string.Empty);
        }

        var result = await channelTrackingService.AddTrackedChannelToManagedChannelAsync(
            new AddManagedChannelTrackedChannelDto(chatId.Value, text),
            cancellationToken);

        return new BotCommandResultDto(result.Success, result.Message);
    }

    private async Task<BotCommandResultDto> ProcessCallbackAsync(TelegramBotUpdateDto update, string languageCode, CancellationToken cancellationToken)
    {
        var sourceUser = update.User!;
        var callbackData = update.CallbackData!;

        if (callbackData == "menu:list")
        {
            return await BuildSubscriptionsListResultAsync(sourceUser.TelegramUserId, languageCode, cancellationToken);
        }

        if (callbackData == "menu:start")
        {
            var resumedCount = await channelTrackingService.SetSubscriptionsActiveAsync(sourceUser.TelegramUserId, true, cancellationToken);
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

        if (callbackData == "menu:plans")
        {
            return await BuildPlansResultAsync(sourceUser, languageCode, cancellationToken);
        }

        if (callbackData == "menu:support")
        {
            return await BuildSupportResultAsync(languageCode, cancellationToken);
        }

        if (callbackData.StartsWith("language:set:", StringComparison.Ordinal))
        {
            var selectedLanguageCode = localizationCatalog.NormalizeLanguageCode(callbackData["language:set:".Length..]);
            var updatedUser = await userService.SetPreferredLanguageAsync(sourceUser.TelegramUserId, selectedLanguageCode, cancellationToken);
            var updatedLanguageCode = localizationCatalog.NormalizeLanguageCode(updatedUser.PreferredLanguageCode);

            return new BotCommandResultDto(
                true,
                messages.BuildLanguageUpdatedMessage(selectedLanguageCode, updatedLanguageCode),
                menuFactory.BuildMainMenu(updatedLanguageCode),
                messages.StartCallbackNotice(updatedLanguageCode));
        }

        if (callbackData.StartsWith("billing:plan:", StringComparison.Ordinal))
        {
            var planCode = callbackData["billing:plan:".Length..];
            var invoiceResult = await billingService.CreatePlanInvoiceAsync(
                new BillingInvoiceRequestDto(sourceUser.TelegramUserId, sourceUser.TelegramUsername, sourceUser.DisplayName, planCode),
                cancellationToken);

            return new BotCommandResultDto(
                invoiceResult.Success,
                invoiceResult.Message,
                menuFactory.BuildMainMenu(languageCode),
                invoiceResult.Success ? messages.StartCallbackNotice(languageCode) : messages.ErrorNotice(languageCode),
                invoiceResult.Invoice);
        }

        if (callbackData.StartsWith("billing:donation:", StringComparison.Ordinal))
        {
            var donationCode = callbackData["billing:donation:".Length..];
            var invoiceResult = await billingService.CreateDonationInvoiceAsync(
                new BillingInvoiceRequestDto(sourceUser.TelegramUserId, sourceUser.TelegramUsername, sourceUser.DisplayName, donationCode),
                cancellationToken);

            return new BotCommandResultDto(
                invoiceResult.Success,
                invoiceResult.Message,
                menuFactory.BuildMainMenu(languageCode),
                invoiceResult.Success ? messages.StartCallbackNotice(languageCode) : messages.ErrorNotice(languageCode),
                invoiceResult.Invoice);
        }

        if (callbackData == "pause_all:confirm")
        {
            var pausedCount = await channelTrackingService.SetSubscriptionsActiveAsync(sourceUser.TelegramUserId, false, cancellationToken);
            return new BotCommandResultDto(
                true,
                messages.BuildPauseAppliedMessage(pausedCount, languageCode),
                menuFactory.BuildMainMenu(languageCode),
                messages.PauseAppliedNotice(languageCode));
        }

        if (callbackData == "delete_all:confirm")
        {
            var removedCount = await channelTrackingService.RemoveAllTrackedChannelsAsync(sourceUser.TelegramUserId, cancellationToken);
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
                new RemoveTrackedChannelByIdDto(sourceUser.TelegramUserId, channelId),
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

            var subscriptions = await channelTrackingService.ListSubscriptionsAsync(sourceUser.TelegramUserId, cancellationToken);
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

    private async Task<BotCommandResultDto> BuildPlansResultAsync(BotUserSnapshotDto sourceUser, string languageCode, CancellationToken cancellationToken)
    {
        var plans = await billingService.ListAvailablePlansAsync(cancellationToken);
        var usage = await billingService.GetSubscriptionUsageAsync(sourceUser.TelegramUserId, cancellationToken);
        return new BotCommandResultDto(
            true,
            messages.BuildPlansMessage(plans, usage, languageCode),
            menuFactory.BuildPlansMenu(plans, languageCode));
    }

    private async Task<BotCommandResultDto> BuildSupportResultAsync(string languageCode, CancellationToken cancellationToken)
    {
        var donations = await billingService.ListAvailableDonationOptionsAsync(cancellationToken);
        return new BotCommandResultDto(
            true,
            messages.BuildSupportProjectMessage(languageCode),
            menuFactory.BuildSupportMenu(donations, languageCode));
    }

    private static bool TryParseChannelId(string value, out Guid channelId) =>
        Guid.TryParse(value, out channelId);

    private async Task RegisterSharedChannelInBackgroundAsync(long telegramUserId, TelegramSharedChatDto sharedChat)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var registrationService = scope.ServiceProvider.GetRequiredService<IMiniAppChannelService>();
            var result = await registrationService.RegisterSharedChannelAsync(telegramUserId, sharedChat, CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                await telegramBotGateway.SendMessageAsync(
                    new TelegramBotOutboundMessageDto(
                        telegramUserId,
                        result.Message,
                        ParseMode: null,
                        DisableWebPagePreview: true),
                    CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Background shared channel registration failed. TelegramUserId={TelegramUserId}, SharedChatId={SharedChatId}",
                telegramUserId,
                sharedChat.ChatId);
        }
    }

    private bool ShouldIgnoreSharedChatFollowup(long telegramUserId, string text)
    {
        if (!RecentSharedChats.TryGetValue(telegramUserId, out var state))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - state.RegisteredAtUtc > SharedChatFollowupWindow)
        {
            RecentSharedChats.TryRemove(telegramUserId, out _);
            return false;
        }

        var normalizedText = text.Trim();
        if (localizationCatalog.IsManagedChannelsRequestLabel(normalizedText))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(state.ChannelTitle) &&
               string.Equals(normalizedText, state.ChannelTitle, StringComparison.OrdinalIgnoreCase);
    }
}
