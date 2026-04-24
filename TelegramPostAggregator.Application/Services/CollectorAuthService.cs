using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Services;

public sealed class CollectorAuthService(
    ICollectorAccountRepository collectorAccountRepository,
    ITelegramCollectorGateway telegramCollectorGateway) : ICollectorAuthService
{
    public async Task<CollectorAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var collector = await GetCollectorAsync(cancellationToken);
        return await telegramCollectorGateway.GetAuthorizationStatusAsync(collector, cancellationToken);
    }

    public async Task<CollectorAuthStatusDto> StartAsync(CancellationToken cancellationToken = default)
    {
        var collector = await GetCollectorAsync(cancellationToken);
        return await telegramCollectorGateway.InitializeAuthenticationAsync(collector, cancellationToken);
    }

    public async Task<CollectorAuthStatusDto> SubmitCodeAsync(SubmitCollectorCodeDto request, CancellationToken cancellationToken = default)
    {
        var collector = await GetCollectorAsync(cancellationToken);
        return await telegramCollectorGateway.SubmitAuthenticationCodeAsync(collector, request.Code, cancellationToken);
    }

    public async Task<CollectorAuthStatusDto> SubmitPasswordAsync(SubmitCollectorPasswordDto request, CancellationToken cancellationToken = default)
    {
        var collector = await GetCollectorAsync(cancellationToken);
        return await telegramCollectorGateway.SubmitAuthenticationPasswordAsync(collector, request.Password, cancellationToken);
    }

    private async Task<Domain.Entities.CollectorAccount> GetCollectorAsync(CancellationToken cancellationToken)
    {
        var collector = await collectorAccountRepository.GetPrimaryAvailableAsync(cancellationToken);
        if (collector is null)
        {
            throw new InvalidOperationException("No active collector account is configured.");
        }

        return collector;
    }
}
