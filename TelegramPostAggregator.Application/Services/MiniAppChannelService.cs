using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services;

public sealed class MiniAppChannelService(
    ISubscriptionRepository subscriptionRepository,
    Abstractions.External.ITelegramBotGateway telegramBotGateway) : IMiniAppChannelService
{
    private static readonly HashSet<string> ReservedUiTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "start", "stop", "subscriptions", "delete all", "language", "confirm stop",
        "yes, delete all", "yes, delete", "cancel", "refresh list", "pause all", "delete",
        "english", "español", "português", "français", "deutsch", "indonesia", "українська",
        "налаштування", "підписка", "назад"
    };

    public async Task<IReadOnlyList<MiniAppChannelDto>> ListAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var candidates = subscriptions
            .Where(subscription => !IsReservedUiChannel(subscription.Channel.ChannelName, subscription.Channel.UsernameOrInviteLink))
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.Channel.TelegramChannelId))
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        var adminChecks = await Task.WhenAll(candidates.Select(async subscription => new
        {
            Subscription = subscription,
            IsBotAdministrator = await telegramBotGateway.IsBotAdministratorAsync(subscription.Channel.TelegramChannelId, cancellationToken)
        }));

        return adminChecks
            .Where(result => result.IsBotAdministrator)
            .Select(result => result.Subscription)
            .OrderByDescending(subscription => subscription.IsActive)
            .ThenBy(subscription => subscription.Channel.ChannelName)
            .Select(subscription => new MiniAppChannelDto(
                subscription.ChannelId,
                subscription.Channel.ChannelName,
                subscription.Channel.UsernameOrInviteLink,
                subscription.Channel.Status.ToString(),
                subscription.IsActive,
                subscription.Channel.LastPostCollectedAtUtc,
                subscription.Channel.LastCollectorError))
            .ToList();
    }

    public async Task<bool> SetActiveAsync(long telegramUserId, Guid channelId, bool isActive, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var target = subscriptions.FirstOrDefault(subscription => subscription.ChannelId == channelId);
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
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(long telegramUserId, Guid channelId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);
        var target = subscriptions.FirstOrDefault(subscription => subscription.ChannelId == channelId);
        if (target is null)
        {
            return false;
        }

        subscriptionRepository.Remove(target);
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private bool IsReservedUiChannel(string channelName, string channelReference) =>
        IsReservedUiText(channelName) || IsReservedUiText(channelReference);

    private static bool IsReservedUiText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ReservedUiTexts.Contains(value.Trim());
    }
}
