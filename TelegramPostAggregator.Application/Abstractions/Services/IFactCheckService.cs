using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IFactCheckService
{
    Task<FactCheckRequestDto> QueueRequestAsync(CreateFactCheckRequestDto request, CancellationToken cancellationToken = default);
    Task ProcessPendingRequestsAsync(CancellationToken cancellationToken = default);
}
