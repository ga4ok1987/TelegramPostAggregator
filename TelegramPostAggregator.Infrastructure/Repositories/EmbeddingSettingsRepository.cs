using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class EmbeddingSettingsRepository(AggregatorDbContext dbContext) : IEmbeddingSettingsRepository
{
    public Task<EmbeddingSettings?> GetAsync(CancellationToken cancellationToken = default) =>
        dbContext.Set<EmbeddingSettings>()
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task AddAsync(EmbeddingSettings settings, CancellationToken cancellationToken = default) =>
        dbContext.Set<EmbeddingSettings>().AddAsync(settings, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
