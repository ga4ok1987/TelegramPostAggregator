using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IFactCheckRequestRepository
{
    Task<FactCheckRequest?> GetByIdAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FactCheckRequest>> GetByStatusAsync(FactCheckStatus status, int take, CancellationToken cancellationToken = default);
    Task AddAsync(FactCheckRequest request, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
