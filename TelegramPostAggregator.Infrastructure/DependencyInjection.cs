using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Infrastructure.Jobs;
using TelegramPostAggregator.Infrastructure.Options;
using TelegramPostAggregator.Infrastructure.Persistence;
using TelegramPostAggregator.Infrastructure.Repositories;
using TelegramPostAggregator.Infrastructure.Services;

namespace TelegramPostAggregator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<TdLibOptions>(configuration.GetSection(TdLibOptions.SectionName));
        services.Configure<CollectorBootstrapOptions>(configuration.GetSection(CollectorBootstrapOptions.SectionName));
        services.Configure<TelegramBotOptions>(configuration.GetSection(TelegramBotOptions.SectionName));

        var connectionString = configuration.GetSection(DatabaseOptions.SectionName).GetValue<string>("ConnectionString")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string is not configured.");

        services.AddDbContext<AggregatorDbContext>(options =>
            options.UseNpgsql(connectionString, builder => builder.MigrationsAssembly(typeof(AggregatorDbContext).Assembly.FullName)));

        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<ITrackedChannelRepository, TrackedChannelRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ICollectorAccountRepository, CollectorAccountRepository>();
        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<IFactCheckRequestRepository, FactCheckRequestRepository>();

        services.AddSingleton<TdLibCollectorClientManager>();
        services.AddSingleton<IImmediateDeliverySignal, ImmediateDeliverySignal>();
        services.AddSingleton<TdLibRealtimePostIngestionService>();
        services.AddHostedService<TdLibCollectorHostedService>();
        services.AddHttpClient(nameof(TelegramBotPollingService));
        services.AddHttpClient(nameof(TelegramFeedDeliveryService));
        services.AddHttpClient(nameof(TelegramBotGateway));
        services.AddHttpClient(nameof(TelegramErrorAlertService));
        services.AddHostedService<TelegramBotPollingService>();
        services.AddHostedService<TelegramFeedDeliveryService>();
        services.AddScoped<ITelegramCollectorGateway, TdLibTelegramCollectorGateway>();
        services.AddSingleton<ITelegramBotGateway, TelegramBotGateway>();
        services.AddSingleton<IErrorAlertService, TelegramErrorAlertService>();
        services.AddScoped<IFactCheckProvider, MockFactCheckProvider>();
        services.AddScoped<CollectorBootstrapper>();

        services.AddScoped<CollectorSubscriptionJob>();
        services.AddScoped<CollectorPostSyncJob>();
        services.AddScoped<FactCheckDispatchJob>();
        services.AddScoped<TdLibMediaCacheCleanupJob>();

        services.AddHangfire(configuration => configuration
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer();

        return services;
    }

    public static IServiceCollection AddMonitoringInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<TelegramBotOptions>(configuration.GetSection(TelegramBotOptions.SectionName));

        var connectionString = configuration.GetSection(DatabaseOptions.SectionName).GetValue<string>("ConnectionString")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string is not configured.");

        services.AddDbContext<AggregatorDbContext>(options =>
            options.UseNpgsql(connectionString, builder => builder.MigrationsAssembly(typeof(AggregatorDbContext).Assembly.FullName)));

        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<ITrackedChannelRepository, TrackedChannelRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ICollectorAccountRepository, CollectorAccountRepository>();
        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<IFactCheckRequestRepository, FactCheckRequestRepository>();

        services.AddHttpClient(nameof(Services.Monitoring.HttpBotStatusProbe));
        services.AddHttpClient(nameof(TelegramBotGateway));
        services.AddScoped<IBotStatusProbe, Services.Monitoring.HeartbeatBotStatusProbe>();
        services.AddScoped<IBotStatusProbe, Services.Monitoring.HttpBotStatusProbe>();
        services.AddSingleton<ITelegramBotGateway, TelegramBotGateway>();
        services.AddScoped<ITelegramMiniAppAuthService, Services.Monitoring.TelegramMiniAppAuthService>();

        return services;
    }

    public static async Task InitializeInfrastructureAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AggregatorDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        var bootstrapper = scope.ServiceProvider.GetRequiredService<CollectorBootstrapper>();
        await bootstrapper.EnsureCollectorAccountAsync(cancellationToken);

        var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobs.AddOrUpdate<CollectorSubscriptionJob>(
            "collector-subscriptions",
            job => job.RunAsync(CancellationToken.None),
            "*/2 * * * *");

        recurringJobs.AddOrUpdate<CollectorPostSyncJob>(
            "collector-sync-posts",
            job => job.RunAsync(CancellationToken.None),
            "*/1 * * * *");

        recurringJobs.AddOrUpdate<FactCheckDispatchJob>(
            "fact-check-dispatch",
            job => job.RunAsync(CancellationToken.None),
            "*/3 * * * *");

        recurringJobs.AddOrUpdate<TdLibMediaCacheCleanupJob>(
            "tdlib-media-cache-cleanup",
            job => job.RunAsync(CancellationToken.None),
            "15 * * * *");
    }
}
