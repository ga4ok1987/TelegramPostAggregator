using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.External;

public interface ITelegramCollectorGateway
{
    Task<CollectorAuthStatusDto> GetAuthorizationStatusAsync(CollectorAccount collectorAccount, CancellationToken cancellationToken = default);
    Task<CollectorAuthStatusDto> InitializeAuthenticationAsync(CollectorAccount collectorAccount, CancellationToken cancellationToken = default);
    Task<CollectorAuthStatusDto> SubmitAuthenticationCodeAsync(CollectorAccount collectorAccount, string code, CancellationToken cancellationToken = default);
    Task<CollectorAuthStatusDto> SubmitAuthenticationPasswordAsync(CollectorAccount collectorAccount, string password, CancellationToken cancellationToken = default);
    Task<CollectorJoinResultDto> EnsureJoinedAsync(CollectorAccount collectorAccount, TrackedChannel channel, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectedPostDto>> GetRecentPostsAsync(
        CollectorAccount collectorAccount,
        TrackedChannel channel,
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken = default);
}
