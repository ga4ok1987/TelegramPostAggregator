using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IServerMetricsProvider
{
    Task<ServerStatusChartDto> GetServerStatusAsync(CancellationToken cancellationToken = default);
}
