using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class OpenAiApiKeyRepository(AggregatorDbContext dbContext) : IOpenAiApiKeyRepository
{
    public async Task<IReadOnlyList<OpenAiApiKey>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Set<OpenAiApiKey>()
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<OpenAiApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Set<OpenAiApiKey>()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<OpenAiApiKey?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        dbContext.Set<OpenAiApiKey>()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(x => x.IsActive, cancellationToken);

    public Task<bool> HasAnyAsync(CancellationToken cancellationToken = default) =>
        dbContext.Set<OpenAiApiKey>().AnyAsync(cancellationToken);

    public Task AddAsync(OpenAiApiKey key, CancellationToken cancellationToken = default) =>
        dbContext.Set<OpenAiApiKey>().AddAsync(key, cancellationToken).AsTask();

    public void Remove(OpenAiApiKey key) =>
        dbContext.Set<OpenAiApiKey>().Remove(key);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
