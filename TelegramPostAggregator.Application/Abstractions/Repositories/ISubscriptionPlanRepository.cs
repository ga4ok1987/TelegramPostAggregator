using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface ISubscriptionPlanRepository
{
    Task<IReadOnlyList<SubscriptionPlanDefinition>> ListAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionPlanDefinition?> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<SubscriptionPlanDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task AddAsync(SubscriptionPlanDefinition plan, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
