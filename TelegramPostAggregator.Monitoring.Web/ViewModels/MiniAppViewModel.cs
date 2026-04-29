using Microsoft.JSInterop;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Monitoring.Web.ViewModels;

public sealed class MiniAppViewModel(
    IMiniAppChannelService miniAppChannelService,
    ITelegramMiniAppAuthService telegramMiniAppAuthService,
    IJSRuntime jsRuntime)
{
    public long? TelegramUserId { get; private set; }
    public string DisplayName { get; private set; } = "Telegram user";
    public string? Username { get; private set; }
    public bool IsTelegramWebApp { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public bool IsLoading { get; private set; } = true;
    public bool IsRefreshing { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? StatusMessage { get; private set; }
    public IReadOnlyList<MiniAppChannelDto> Channels { get; private set; } = [];
    private HashSet<Guid> PendingChannelIds { get; } = [];
    private HashSet<Guid> PendingSubscriptionIds { get; } = [];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        IsAuthenticated = false;
        ErrorMessage = null;
        StatusMessage = null;
        Channels = [];

        try
        {
            var context = await jsRuntime.InvokeAsync<TelegramMiniAppContext>("channelsMonitorMiniApp.getContext");
            IsTelegramWebApp = context.IsTelegramWebApp;

            if (!IsTelegramWebApp || string.IsNullOrWhiteSpace(context.InitData))
            {
                TelegramUserId = null;
                DisplayName = "Telegram Mini App";
                ErrorMessage = "Open this screen from the Telegram bot menu.";
                return;
            }

            var authResult = await telegramMiniAppAuthService.AuthenticateAsync(context.InitData, cancellationToken);
            if (!authResult.IsAuthenticated || authResult.TelegramUserId is null or 0)
            {
                TelegramUserId = null;
                DisplayName = "Telegram Mini App";
                ErrorMessage = authResult.ErrorMessage ?? "Telegram authorization failed.";
                return;
            }

            TelegramUserId = authResult.TelegramUserId;
            Username = authResult.Username;
            DisplayName = BuildDisplayName(authResult.FirstName, authResult.LastName, authResult.Username);
            IsAuthenticated = true;

            await RefreshChannelsAsync(cancellationToken);
        }
        catch (JSException)
        {
            TelegramUserId = null;
            DisplayName = "Telegram Mini App";
            ErrorMessage = "Telegram Mini App context is unavailable. Open this screen from the bot.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshChannelsAsync(CancellationToken cancellationToken = default)
    {
        if (TelegramUserId is null or 0)
        {
            Channels = [];
            return;
        }

        IsRefreshing = true;
        try
        {
            Channels = await miniAppChannelService.ListAsync(TelegramUserId.Value, cancellationToken);
            ErrorMessage = null;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public async Task<bool> StartChannelAsync(Guid channelId, CancellationToken cancellationToken = default) =>
        await UpdateChannelAsync(channelId, true, "Channel monitoring started.", cancellationToken);

    public async Task<bool> StopChannelAsync(Guid channelId, CancellationToken cancellationToken = default) =>
        await UpdateChannelAsync(channelId, false, "Channel monitoring paused.", cancellationToken);

    public async Task<bool> DeleteChannelAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        if (TelegramUserId is null or 0)
        {
            ErrorMessage = "Telegram user is not available.";
            return false;
        }

        if (IsChannelBusy(channelId))
        {
            return false;
        }

        PendingChannelIds.Add(channelId);

        try
        {
            var deleted = await miniAppChannelService.DeleteAsync(TelegramUserId.Value, channelId, cancellationToken);
            if (!deleted)
            {
                ErrorMessage = "Channel was not found.";
                return false;
            }

            StatusMessage = "Channel removed from your list.";
            ErrorMessage = null;
            await RefreshChannelsAsync(cancellationToken);
            return true;
        }
        finally
        {
            PendingChannelIds.Remove(channelId);
        }
    }

    public ValueTask ShowPlaceholderAsync(string message) =>
        jsRuntime.InvokeVoidAsync("channelsMonitorMiniApp.showAlert", message);

    public bool IsChannelBusy(Guid channelId) => PendingChannelIds.Contains(channelId);

    public bool IsSubscriptionBusy(Guid subscriptionId) => PendingSubscriptionIds.Contains(subscriptionId);

    private async Task<bool> UpdateChannelAsync(Guid channelId, bool isActive, string successMessage, CancellationToken cancellationToken)
    {
        if (TelegramUserId is null or 0)
        {
            ErrorMessage = "Telegram user is not available.";
            return false;
        }

        if (IsChannelBusy(channelId))
        {
            return false;
        }

        PendingChannelIds.Add(channelId);

        try
        {
            var updated = await miniAppChannelService.SetActiveAsync(TelegramUserId.Value, channelId, isActive, cancellationToken);
            if (!updated)
            {
                ErrorMessage = "Channel was not found.";
                return false;
            }

            StatusMessage = successMessage;
            ErrorMessage = null;
            await RefreshChannelsAsync(cancellationToken);
            return true;
        }
        finally
        {
            PendingChannelIds.Remove(channelId);
        }
    }

    public async Task<bool> StartSubscriptionAsync(Guid managedChannelId, Guid subscriptionId, CancellationToken cancellationToken = default) =>
        await UpdateSubscriptionAsync(managedChannelId, subscriptionId, true, "Subscription started.", cancellationToken);

    public async Task<bool> StopSubscriptionAsync(Guid managedChannelId, Guid subscriptionId, CancellationToken cancellationToken = default) =>
        await UpdateSubscriptionAsync(managedChannelId, subscriptionId, false, "Subscription paused.", cancellationToken);

    public async Task<bool> DeleteSubscriptionAsync(Guid managedChannelId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        if (TelegramUserId is null or 0)
        {
            ErrorMessage = "Telegram user is not available.";
            return false;
        }

        if (IsSubscriptionBusy(subscriptionId))
        {
            return false;
        }

        PendingSubscriptionIds.Add(subscriptionId);

        try
        {
            var deleted = await miniAppChannelService.DeleteSubscriptionAsync(TelegramUserId.Value, managedChannelId, subscriptionId, cancellationToken);
            if (!deleted)
            {
                ErrorMessage = "Subscription was not found.";
                return false;
            }

            StatusMessage = "Subscription removed.";
            ErrorMessage = null;
            await RefreshChannelsAsync(cancellationToken);
            return true;
        }
        finally
        {
            PendingSubscriptionIds.Remove(subscriptionId);
        }
    }

    private async Task<bool> UpdateSubscriptionAsync(Guid managedChannelId, Guid subscriptionId, bool isActive, string successMessage, CancellationToken cancellationToken)
    {
        if (TelegramUserId is null or 0)
        {
            ErrorMessage = "Telegram user is not available.";
            return false;
        }

        if (IsSubscriptionBusy(subscriptionId))
        {
            return false;
        }

        PendingSubscriptionIds.Add(subscriptionId);

        try
        {
            var updated = await miniAppChannelService.SetSubscriptionActiveAsync(TelegramUserId.Value, managedChannelId, subscriptionId, isActive, cancellationToken);
            if (!updated)
            {
                ErrorMessage = "Subscription was not found.";
                return false;
            }

            StatusMessage = successMessage;
            ErrorMessage = null;
            await RefreshChannelsAsync(cancellationToken);
            return true;
        }
        finally
        {
            PendingSubscriptionIds.Remove(subscriptionId);
        }
    }

    private static string BuildDisplayName(string? firstName, string? lastName, string? username)
    {
        var fullName = string.Join(" ", new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            return $"@{username.TrimStart('@')}";
        }

        return "Telegram user";
    }

    public sealed class TelegramMiniAppContext
    {
        public string? InitData { get; set; }
        public bool IsTelegramWebApp { get; set; }
    }
}
