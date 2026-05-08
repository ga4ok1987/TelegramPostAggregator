using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Monitoring.Web.ViewModels;

public sealed class ClientAdminViewModel(IClientAdminService clientAdminService)
{
    private const int PageSize = 10;

    public IReadOnlyList<AdminClientDto> Clients { get; private set; } = [];
    public AdminClientDetailDto? SelectedClientDetail { get; private set; }
    public AdminPagedResultDto<AdminBotSubscriptionDto>? BotSubscriptionsPage { get; private set; }
    public AdminPagedResultDto<AdminManagedChannelSourceSubscriptionDto>? ManagedChannelSubscriptionsPage { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsLoadingDetail { get; private set; }
    public bool IsLoadingBotSubscriptions { get; private set; }
    public bool IsLoadingManagedChannelSubscriptions { get; private set; }
    public string? ErrorMessage { get; private set; }
    public Guid? BusyUserId { get; private set; }
    public Guid? SelectedUserId { get; private set; }
    public Guid? ExpandedManagedChannelId { get; private set; }
    public bool IsBotSubscriptionsExpanded { get; private set; }
    public string SearchTerm { get; set; } = string.Empty;

    public IReadOnlyList<AdminClientDto> FilteredClients =>
        string.IsNullOrWhiteSpace(SearchTerm)
            ? Clients
            : Clients.Where(client =>
                client.DisplayName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                client.TelegramUsername.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                client.TelegramUserId.ToString().Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public AdminClientDto? SelectedClient =>
        FilteredClients.FirstOrDefault(client => client.UserId == SelectedUserId);

    public async Task SelectClientAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        SelectedUserId = userId;
        ResetExpandedSections();
        await LoadSelectedClientDetailAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Clients = await clientAdminService.ListAsync(cancellationToken);
            EnsureSelectedClient();
            ResetExpandedSections();
            await LoadSelectedClientDetailAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<bool> SetBlockedAsync(Guid userId, bool isBlocked, CancellationToken cancellationToken = default)
    {
        BusyUserId = userId;
        ErrorMessage = null;

        try
        {
            var updated = await clientAdminService.SetBlockedAsync(userId, isBlocked, cancellationToken);
            if (!updated)
            {
                ErrorMessage = "Client was not found.";
                return false;
            }

            await RefreshAsync(cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return false;
        }
        finally
        {
            BusyUserId = null;
        }
    }

    public async Task LoadSelectedClientDetailAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedUserId is null)
        {
            SelectedClientDetail = null;
            return;
        }

        IsLoadingDetail = true;

        try
        {
            SelectedClientDetail = await clientAdminService.GetAsync(SelectedUserId.Value, cancellationToken);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            SelectedClientDetail = null;
        }
        finally
        {
            IsLoadingDetail = false;
        }
    }

    public async Task ToggleBotSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        IsBotSubscriptionsExpanded = !IsBotSubscriptionsExpanded;
        if (IsBotSubscriptionsExpanded)
        {
            await LoadBotSubscriptionsPageAsync(1, cancellationToken);
        }
    }

    public async Task LoadBotSubscriptionsPageAsync(int page, CancellationToken cancellationToken = default)
    {
        if (SelectedUserId is null)
        {
            BotSubscriptionsPage = null;
            return;
        }

        IsLoadingBotSubscriptions = true;

        try
        {
            BotSubscriptionsPage = await clientAdminService.GetBotSubscriptionsPageAsync(SelectedUserId.Value, page, PageSize, cancellationToken);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            BotSubscriptionsPage = null;
        }
        finally
        {
            IsLoadingBotSubscriptions = false;
        }
    }

    public async Task ToggleManagedChannelSubscriptionsAsync(Guid managedChannelId, CancellationToken cancellationToken = default)
    {
        if (ExpandedManagedChannelId == managedChannelId)
        {
            ExpandedManagedChannelId = null;
            ManagedChannelSubscriptionsPage = null;
            return;
        }

        ExpandedManagedChannelId = managedChannelId;
        await LoadManagedChannelSubscriptionsPageAsync(1, cancellationToken);
    }

    public async Task LoadManagedChannelSubscriptionsPageAsync(int page, CancellationToken cancellationToken = default)
    {
        if (ExpandedManagedChannelId is null)
        {
            ManagedChannelSubscriptionsPage = null;
            return;
        }

        IsLoadingManagedChannelSubscriptions = true;

        try
        {
            ManagedChannelSubscriptionsPage = await clientAdminService.GetManagedChannelSubscriptionsPageAsync(ExpandedManagedChannelId.Value, page, PageSize, cancellationToken);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            ManagedChannelSubscriptionsPage = null;
        }
        finally
        {
            IsLoadingManagedChannelSubscriptions = false;
        }
    }

    private void EnsureSelectedClient()
    {
        if (FilteredClients.Count == 0)
        {
            SelectedUserId = null;
            SelectedClientDetail = null;
            ResetExpandedSections();
            return;
        }

        if (SelectedUserId is not null && FilteredClients.Any(client => client.UserId == SelectedUserId))
        {
            return;
        }

        SelectedUserId = FilteredClients[0].UserId;
    }

    private void ResetExpandedSections()
    {
        IsBotSubscriptionsExpanded = false;
        BotSubscriptionsPage = null;
        ExpandedManagedChannelId = null;
        ManagedChannelSubscriptionsPage = null;
    }
}
