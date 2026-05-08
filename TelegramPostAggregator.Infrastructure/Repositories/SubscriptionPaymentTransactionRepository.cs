using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Repositories;

public sealed class SubscriptionPaymentTransactionRepository(AggregatorDbContext dbContext) : ISubscriptionPaymentTransactionRepository
{
    public Task<SubscriptionPaymentTransaction?> GetByPayloadTokenAsync(string payloadToken, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPaymentTransactions
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.PayloadToken == payloadToken, cancellationToken);

    public Task<SubscriptionPaymentTransaction?> GetByTelegramChargeIdAsync(string telegramPaymentChargeId, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPaymentTransactions
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TelegramPaymentChargeId == telegramPaymentChargeId, cancellationToken);

    public Task AddAsync(SubscriptionPaymentTransaction transaction, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPaymentTransactions.AddAsync(transaction, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
