using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.Repositories;

public interface ISubscriptionPaymentTransactionRepository
{
    Task<SubscriptionPaymentTransaction?> GetByPayloadTokenAsync(string payloadToken, CancellationToken cancellationToken = default);
    Task<SubscriptionPaymentTransaction?> GetByTelegramChargeIdAsync(string telegramPaymentChargeId, CancellationToken cancellationToken = default);
    Task AddAsync(SubscriptionPaymentTransaction transaction, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
