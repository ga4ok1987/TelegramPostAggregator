using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class DonationOptionRepository(AggregatorDbContext dbContext) : IDonationOptionRepository
{
    public async Task<IReadOnlyList<DonationOption>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.DonationOptions
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

    public Task<DonationOption?> GetByIdAsync(Guid donationId, CancellationToken cancellationToken = default) =>
        dbContext.DonationOptions.FirstOrDefaultAsync(x => x.Id == donationId, cancellationToken);

    public Task<DonationOption?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.DonationOptions.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

    public Task AddAsync(DonationOption donation, CancellationToken cancellationToken = default) =>
        dbContext.DonationOptions.AddAsync(donation, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
