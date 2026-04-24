using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface ICollectorAuthService
{
    Task<CollectorAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<CollectorAuthStatusDto> StartAsync(CancellationToken cancellationToken = default);
    Task<CollectorAuthStatusDto> SubmitCodeAsync(SubmitCollectorCodeDto request, CancellationToken cancellationToken = default);
    Task<CollectorAuthStatusDto> SubmitPasswordAsync(SubmitCollectorPasswordDto request, CancellationToken cancellationToken = default);
}
