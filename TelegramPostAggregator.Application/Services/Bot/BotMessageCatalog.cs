using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services.Bot;

public sealed class BotMessageCatalog
{
    public string EmptyUpdatePrompt => "Оберіть дію з меню нижче.";

    public string BuildStartMessage(int resumedCount) =>
        resumedCount > 0
            ? $"Поновив {resumedCount} підписок. Надішліть посилання на Telegram-канал або відкрийте список підписок."
            : "Бот активний. Надішліть посилання на Telegram-канал або відкрийте список підписок.";

    public string PauseConfirmationPrompt => "Поставити всі підписки на паузу?";

    public string PauseConfirmationCallbackNotice => "Підтвердіть зупинку або скасуйте.";

    public string DeleteAllConfirmationPrompt => "Видалити всі підписки?";

    public string DeleteAllConfirmationCallbackNotice => "Потрібне підтвердження.";

    public string RemoveUsage => "Використання: /remove <channel>";

    public string BuildSubscriptionDisabledMessage(string channelReference) =>
        $"Підписку для {channelReference} вимкнено.";

    public string BuildSubscriptionAddedMessage(string channelReference) =>
        $"Підписку додано: {channelReference}";

    public string BuildStartCallbackMessage(int resumedCount) =>
        resumedCount > 0 ? $"Поновив {resumedCount} підписок." : "Активних змін немає. Бот уже працює.";

    public string PauseAppliedNotice => "Пауза застосована.";

    public string BuildPauseAppliedMessage(int pausedCount) =>
        pausedCount > 0 ? $"Поставив на паузу {pausedCount} підписок." : "Активних підписок для паузи немає.";

    public string DeletionCompletedNotice => "Видалення завершено.";

    public string BuildDeleteAllAppliedMessage(int removedCount) =>
        removedCount > 0 ? $"Видалено {removedCount} підписок." : "Немає підписок для видалення.";

    public string ActionCancelledMessage => "Дію скасовано.";

    public string ActionCancelledNotice => "Скасовано.";

    public string UnknownActionMessage => "Невідома дія.";

    public string UnknownActionNotice => "Невідома дія.";

    public string InvalidSubscriptionMessage => "Не вдалося розпізнати підписку.";

    public string ErrorNotice => "Помилка.";

    public string SubscriptionNotFoundMessage => "Підписку не знайдено.";

    public string SubscriptionNotFoundNotice => "Не знайдено.";

    public string SubscriptionDeletedMessage => "Підписку видалено.";

    public string SubscriptionDeletedNotice => "Видалено.";

    public string BuildDeleteOneConfirmationMessage(SubscriptionDto subscription) =>
        $"Видалити підписку {subscription.ChannelName}?";

    public string EmptySubscriptionsMessage => "Підписок ще немає. Надішліть посилання на канал, щоб додати його.";

    public string SubscriptionsListUpdatedNotice => "Список оновлено.";

    public string BuildSubscriptionsListMessage(IReadOnlyList<SubscriptionDto> subscriptions)
    {
        var lines = subscriptions
            .Select((subscription, index) => $"{index + 1}. {(subscription.IsActive ? "🟢" : "⏸")} {subscription.ChannelName}")
            .ToList();

        return "Ваші підписки:\n" + string.Join(Environment.NewLine, lines);
    }
}
