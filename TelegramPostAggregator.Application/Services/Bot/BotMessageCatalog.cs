using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services.Bot;

public sealed class BotMessageCatalog(BotLocalizationCatalog localizationCatalog)
{
    private const string GreenCircle = "\U0001F7E2";
    private const string PauseIcon = "\u23F8";

    public string EmptyUpdatePrompt(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).EmptyUpdatePrompt;

    public string BuildStartMessage(int resumedCount, string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        return resumedCount > 0
            ? string.Format(locale.StartMessageWithResumedTemplate, resumedCount)
            : locale.StartMessageWithoutResumed;
    }

    public string PauseConfirmationPrompt(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).PauseConfirmationPrompt;

    public string PauseConfirmationCallbackNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).PauseConfirmationCallbackNotice;

    public string DeleteAllConfirmationPrompt(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).DeleteAllConfirmationPrompt;

    public string DeleteAllConfirmationCallbackNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).DeleteAllConfirmationCallbackNotice;

    public string DeleteOneConfirmationCallbackNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).DeleteOneConfirmationCallbackNotice;

    public string RemoveUsage(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).RemoveUsage;

    public string BuildSubscriptionDisabledMessage(string channelReference, string languageCode) =>
        string.Format(localizationCatalog.GetLocale(languageCode).SubscriptionDisabledTemplate, channelReference);

    public string BuildSubscriptionAddedMessage(string channelReference, string languageCode) =>
        string.Format(localizationCatalog.GetLocale(languageCode).SubscriptionAddedTemplate, channelReference);

    public string BuildStartCallbackMessage(int resumedCount, string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        return resumedCount > 0
            ? string.Format(locale.StartCallbackWithResumedTemplate, resumedCount)
            : locale.StartCallbackWithoutChanges;
    }

    public string StartCallbackNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).StartCallbackNotice;

    public string PauseAppliedNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).PauseAppliedNotice;

    public string BuildPauseAppliedMessage(int pausedCount, string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        return pausedCount > 0
            ? string.Format(locale.PauseAppliedTemplate, pausedCount)
            : locale.PauseAppliedWithoutChanges;
    }

    public string DeletionCompletedNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).DeletionCompletedNotice;

    public string BuildDeleteAllAppliedMessage(int removedCount, string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        return removedCount > 0
            ? string.Format(locale.DeleteAllAppliedTemplate, removedCount)
            : locale.DeleteAllAppliedWithoutChanges;
    }

    public string ActionCancelledMessage(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).ActionCancelledMessage;

    public string ActionCancelledNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).ActionCancelledNotice;

    public string UnknownActionMessage(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).UnknownActionMessage;

    public string UnknownActionNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).UnknownActionNotice;

    public string InvalidSubscriptionMessage(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).InvalidSubscriptionMessage;

    public string ErrorNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).ErrorNotice;

    public string SubscriptionNotFoundMessage(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).SubscriptionNotFoundMessage;

    public string SubscriptionNotFoundNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).SubscriptionNotFoundNotice;

    public string SubscriptionDeletedMessage(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).SubscriptionDeletedMessage;

    public string SubscriptionDeletedNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).SubscriptionDeletedNotice;

    public string BuildDeleteOneConfirmationMessage(SubscriptionDto subscription, string languageCode) =>
        string.Format(localizationCatalog.GetLocale(languageCode).DeleteOneConfirmationTemplate, subscription.ChannelName);

    public string EmptySubscriptionsMessage(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).EmptySubscriptionsMessage;

    public string SubscriptionsListUpdatedNotice(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).SubscriptionsListUpdatedNotice;

    public string LanguageSelectionPrompt(string languageCode) =>
        localizationCatalog.GetLocale(languageCode).LanguageSelectionPrompt;

    public string ManagedChannelsPrompt() =>
        "Use the channel picker button to select a private channel where the bot is already an administrator.";

    public string BuildBotFaqMessage(string languageCode)
    {
        return localizationCatalog.NormalizeLanguageCode(languageCode) switch
        {
            "uk" =>
                """
                FAQ

                1. Як додати звичайну підписку для себе:
                Надішліть боту посилання на Telegram-канал або @username. Бот додасть його у ваш список підписок.

                2. Як переглянути або видалити свої підписки:
                Натисніть «Список підписок». Там можна оновити список, поставити все на паузу або видалити окремі підписки.

                3. Як працює Старт / Стоп:
                «Старт» відновлює моніторинг нових постів. «Стоп» ставить доставку на паузу. Пропущені під час паузи пости потім не доганяються.

                4. Як підключити власний канал:
                Додайте бота адміністратором у свій канал, потім у чаті з ботом натисніть «Add my channel» і виберіть цей канал.

                5. Як додати підписку у власний канал:
                Після підключення каналу надішліть у цей канал одним окремим повідомленням посилання на джерело або @username. Бот почне пересилати нові пости саме туди.

                6. Де керувати власними каналами:
                Відкрийте Mini App. Там можна переглядати свої канали, зупиняти їх, видаляти та керувати підписками всередині кожного каналу.
                """,
            _ =>
                """
                FAQ

                1. How to add a personal subscription:
                Send the bot a Telegram channel link or @username. The bot will add it to your subscriptions.

                2. How to view or remove subscriptions:
                Open Subscriptions. There you can refresh the list, pause everything, or remove individual subscriptions.

                3. How Start / Stop works:
                Start resumes monitoring for new posts. Stop pauses delivery. Posts missed during pause are not replayed later.

                4. How to connect your own channel:
                Add the bot as an administrator in your channel, then press Add my channel in the bot chat and choose that channel.

                5. How to add a source subscription into your own channel:
                After the channel is connected, send a single message into that channel containing a source link or @username. The bot will start delivering new posts there.

                6. Where to manage your own channels:
                Open the Mini App. There you can review your channels, stop them, delete them, and manage source subscriptions inside each channel.
                """
        };
    }

    public string BuildLanguageUpdatedMessage(string selectedLanguageCode, string currentLanguageCode)
    {
        var locale = localizationCatalog.GetLocale(currentLanguageCode);
        var selected = localizationCatalog.GetLocale(selectedLanguageCode);
        return string.Format(locale.LanguageUpdatedTemplate, $"{selected.Flag} {selected.Name}");
    }

    public string BuildSubscriptionsListMessage(IReadOnlyList<SubscriptionDto> subscriptions, string languageCode)
    {
        var locale = localizationCatalog.GetLocale(languageCode);
        var lines = subscriptions
            .Select((subscription, index) => $"{index + 1}. {(subscription.IsActive ? GreenCircle : PauseIcon)} {subscription.ChannelName}")
            .ToList();

        return locale.SubscriptionsListTitle + "\n" + string.Join(Environment.NewLine, lines);
    }
}
