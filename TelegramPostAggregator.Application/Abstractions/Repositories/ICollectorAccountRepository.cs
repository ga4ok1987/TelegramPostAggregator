using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface ICollectorAccountRepository
{
    Task<CollectorAccount?> GetPrimaryAvailableAsync(CancellationToken cancellationToken = default);
    Task<CollectorAccount?> GetByIdAsync(Guid collectorAccountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChannelCollectorAssignment>> GetPendingAssignmentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChannelCollectorAssignment>> GetAssignmentsForSynchronizationAsync(CancellationToken cancellationToken = default);
    Task AddAssignmentAsync(ChannelCollectorAssignment assignment, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
