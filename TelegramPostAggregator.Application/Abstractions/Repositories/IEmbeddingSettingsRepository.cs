using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IEmbeddingSettingsRepository
{
    Task<EmbeddingSettings?> GetAsync(CancellationToken cancellationToken = default);
    Task AddAsync(EmbeddingSettings settings, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
