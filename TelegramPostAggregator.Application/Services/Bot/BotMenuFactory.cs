using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services.Bot;

public sealed class BotMenuFactory
{
    public const string StartLabel = "Старт";
    public const string StopLabel = "Стоп";
    public const string ListLabel = "Список підписок";
    public const string DeleteAllLabel = "Видалити всі";

    public BotReplyMarkupDto BuildMainMenu() =>
        new(
            [
                [new BotButtonDto(StartLabel), new BotButtonDto(StopLabel)],
                [new BotButtonDto(ListLabel)],
                [new BotButtonDto(DeleteAllLabel)]
            ]);

    public BotReplyMarkupDto BuildPauseConfirmationMenu() =>
        new(
            [
                [new BotButtonDto("Підтвердити стоп", "pause_all:confirm")],
                [new BotButtonDto("Скасувати", "action:cancel")]
            ],
            IsInline: true);

    public BotReplyMarkupDto BuildDeleteAllConfirmationMenu() =>
        new(
            [
                [new BotButtonDto("Так, видалити всі", "delete_all:confirm")],
                [new BotButtonDto("Скасувати", "action:cancel")]
            ],
            IsInline: true);

    public BotReplyMarkupDto BuildDeleteOneConfirmationMenu(Guid channelId) =>
        new(
            [
                [new BotButtonDto("Так, видалити", $"delete_one:confirm:{channelId}")],
                [new BotButtonDto("Скасувати", "action:cancel")]
            ],
            IsInline: true);

    public BotReplyMarkupDto BuildSubscriptionsMenu(IReadOnlyList<SubscriptionDto> subscriptions)
    {
        var buttons = subscriptions
            .Select(subscription => (IReadOnlyList<BotButtonDto>)[new BotButtonDto($"Видалити {subscription.ChannelName}", $"delete_one:{subscription.ChannelId}")])
            .ToList();

        buttons.Add([new BotButtonDto("Оновити список", "menu:list")]);
        buttons.Add([new BotButtonDto("Поставити все на паузу", "menu:stop")]);
        buttons.Add([new BotButtonDto("Видалити всі", "menu:delete_all")]);

        return new BotReplyMarkupDto(buttons, IsInline: true);
    }
}
