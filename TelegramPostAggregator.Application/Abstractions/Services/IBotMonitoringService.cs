using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IBotMonitoringService
{
    Task<MonitoringDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
}
