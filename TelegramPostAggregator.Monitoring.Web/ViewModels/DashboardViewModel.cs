using Microsoft.Extensions.Options;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;

namespace TelegramPostAggregator.Monitoring.Web.ViewModels;

public sealed class DashboardViewModel
{
    private readonly IBotMonitoringService _botMonitoringService;
    private readonly IOptionsMonitor<BotMonitoringOptions> _optionsMonitor;

    public DashboardViewModel(IBotMonitoringService botMonitoringService, IOptionsMonitor<BotMonitoringOptions> optionsMonitor)
    {
        _botMonitoringService = botMonitoringService;
        _optionsMonitor = optionsMonitor;
    }

    public MonitoringDashboardDto? Dashboard { get; private set; }

    public bool IsLoading { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int RefreshIntervalSeconds => Math.Max(5, _optionsMonitor.CurrentValue.RefreshIntervalSeconds);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Dashboard = await _botMonitoringService.GetDashboardAsync(cancellationToken);
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
}
