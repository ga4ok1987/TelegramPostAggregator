using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TelegramPostAggregator.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AggregatorDbContext>
{
    public AggregatorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TPA_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=telegram_post_aggregator;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AggregatorDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new AggregatorDbContext(optionsBuilder.Options);
    }
}
