using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.Services.Bot;
using TelegramPostAggregator.Application.Options;
using TelegramPostAggregator.Application.Services;

namespace TelegramPostAggregator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CollectorOptions>(configuration.GetSection(CollectorOptions.SectionName));
        services.Configure<FactCheckOptions>(configuration.GetSection(FactCheckOptions.SectionName));
        services.Configure<MiniAppOptions>(configuration.GetSection(MiniAppOptions.SectionName));

        services.AddScoped<IChannelKeyNormalizer, ChannelKeyNormalizer>();
        services.AddScoped<ITextNormalizer, TextNormalizer>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IChannelTrackingService, ChannelTrackingService>();
        services.AddScoped<IMiniAppChannelService, MiniAppChannelService>();
        services.AddScoped<IFeedService, FeedService>();
        services.AddScoped<IFactCheckService, FactCheckService>();
        services.AddScoped<ICollectorCoordinator, CollectorCoordinator>();
        services.AddScoped<ICollectorAuthService, CollectorAuthService>();
        services.AddScoped<IBotUpdateProcessor, BotUpdateProcessor>();
        services.AddSingleton<BotLocalizationCatalog>();
        services.AddSingleton<BotMenuFactory>();
        services.AddSingleton<BotMessageCatalog>();

        return services;
    }

    public static IServiceCollection AddMonitoringApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplication(configuration);
        services.Configure<BotMonitoringOptions>(configuration.GetSection(BotMonitoringOptions.SectionName));
        services.AddScoped<IBotMonitoringService, BotMonitoringService>();
        return services;
    }
}
