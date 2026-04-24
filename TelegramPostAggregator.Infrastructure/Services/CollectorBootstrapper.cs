using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using TelegramPostAggregator.Infrastructure.Options;
using TelegramPostAggregator.Infrastructure.Persistence;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class CollectorBootstrapper(
    AggregatorDbContext dbContext,
    IOptions<CollectorBootstrapOptions> options,
    ILogger<CollectorBootstrapper> logger)
{
    public async Task EnsureCollectorAccountAsync(CancellationToken cancellationToken = default)
    {
        var externalKey = options.Value.ExternalAccountKey.Trim();
        if (string.IsNullOrWhiteSpace(externalKey))
        {
            return;
        }

        var existing = await dbContext.CollectorAccounts.FirstOrDefaultAsync(x => x.ExternalAccountKey == externalKey, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var collector = new CollectorAccount
        {
            Name = options.Value.Name,
            ExternalAccountKey = externalKey,
            PhoneNumber = options.Value.PhoneNumber,
            Status = CollectorAccountStatus.Active,
            IsEnabled = true,
            Priority = 0
        };

        await dbContext.CollectorAccounts.AddAsync(collector, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded default collector account {CollectorName}", collector.Name);
    }
}
