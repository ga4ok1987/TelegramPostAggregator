using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class CollectorAccountRepository(AggregatorDbContext dbContext) : ICollectorAccountRepository
{
    public Task<CollectorAccount?> GetPrimaryAvailableAsync(CancellationToken cancellationToken = default) =>
        dbContext.CollectorAccounts
            .Where(x => x.IsEnabled && x.Status == CollectorAccountStatus.Active)
            .OrderBy(x => x.Priority)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<CollectorAccount?> GetByIdAsync(Guid collectorAccountId, CancellationToken cancellationToken = default) =>
        dbContext.CollectorAccounts.FirstOrDefaultAsync(x => x.Id == collectorAccountId, cancellationToken);

    public async Task<IReadOnlyList<ChannelCollectorAssignment>> GetPendingAssignmentsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ChannelCollectorAssignments
            .Include(x => x.Channel)
            .Include(x => x.CollectorAccount)
            .Where(x => x.Status == ChannelTrackingStatus.PendingSubscription && x.CollectorAccount.IsEnabled)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ChannelCollectorAssignment>> GetAssignmentsForSynchronizationAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ChannelCollectorAssignments
            .Include(x => x.Channel)
            .Include(x => x.CollectorAccount)
            .Where(x => x.Status == ChannelTrackingStatus.Active && x.CollectorAccount.IsEnabled)
            .OrderBy(x => x.LastSyncedAtUtc ?? DateTimeOffset.MinValue)
            .ToListAsync(cancellationToken);

    public async Task AddAssignmentAsync(ChannelCollectorAssignment assignment, CancellationToken cancellationToken = default) =>
        await dbContext.ChannelCollectorAssignments.AddAsync(assignment, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
