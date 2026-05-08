using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class SubscriptionPlanRepository(AggregatorDbContext dbContext) : ISubscriptionPlanRepository
{
    public async Task<IReadOnlyList<SubscriptionPlanDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.SubscriptionPlanDefinitions
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

    public Task<SubscriptionPlanDefinition?> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlanDefinitions.FirstOrDefaultAsync(x => x.Id == planId, cancellationToken);

    public Task<SubscriptionPlanDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlanDefinitions.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

    public Task AddAsync(SubscriptionPlanDefinition plan, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlanDefinitions.AddAsync(plan, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
