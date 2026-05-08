using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface IDonationOptionRepository
{
    Task<IReadOnlyList<DonationOption>> ListAsync(CancellationToken cancellationToken = default);
    Task<DonationOption?> GetByIdAsync(Guid donationId, CancellationToken cancellationToken = default);
    Task<DonationOption?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task AddAsync(DonationOption donation, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
