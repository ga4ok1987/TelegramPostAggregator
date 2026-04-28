using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services;

public sealed class MiniAppChannelService(
    ISubscriptionRepository subscriptionRepository,
    Bot.BotLocalizationCatalog localizationCatalog) : IMiniAppChannelService
{
    public async Task<IReadOnlyList<MiniAppChannelDto>> ListAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetByUserTelegramIdAsync(telegramUserId, cancellationToken);

        return subscriptions
            .Where(subscription => !IsReservedUiChannel(subscription.Channel.ChannelName, subscription.Channel.UsernameOrInviteLink))
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
        localizationCatalog.IsReservedUiText(channelName) || localizationCatalog.IsReservedUiText(channelReference);
}
