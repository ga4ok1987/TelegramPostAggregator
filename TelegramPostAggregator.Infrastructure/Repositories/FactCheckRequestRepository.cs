using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class FactCheckRequestRepository(AggregatorDbContext dbContext) : IFactCheckRequestRepository
{
    public Task<FactCheckRequest?> GetByIdAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        dbContext.FactCheckRequests.FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);

    public async Task<IReadOnlyList<FactCheckRequest>> GetByStatusAsync(FactCheckStatus status, int take, CancellationToken cancellationToken = default) =>
        await dbContext.FactCheckRequests
            .Where(x => x.Status == status)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(FactCheckRequest request, CancellationToken cancellationToken = default) =>
        await dbContext.FactCheckRequests.AddAsync(request, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
