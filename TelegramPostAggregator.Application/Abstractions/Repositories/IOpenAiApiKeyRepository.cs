using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IOpenAiApiKeyRepository
{
    Task<IReadOnlyList<OpenAiApiKey>> ListAsync(CancellationToken cancellationToken = default);
    Task<OpenAiApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OpenAiApiKey?> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<bool> HasAnyAsync(CancellationToken cancellationToken = default);
    Task AddAsync(OpenAiApiKey key, CancellationToken cancellationToken = default);
    void Remove(OpenAiApiKey key);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
