using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Services;
using TelegramPostAggregator.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TelegramPostAggregator.Application.Tests;

public sealed class MiniAppChannelServiceTests
{
    [Fact]
    public async Task ListAsync_Filters_Out_Channels_Where_Bot_Is_No_Longer_Admin()
    {
        var service = new MiniAppChannelService(
            new FakeManagedChannelRepository(
            [
                CreateManagedChannel("Alpha", -1001, "alpha", true),
                CreateManagedChannel("Beta", -1002, "beta", true)
            ]),
            new FakeManagedChannelSubscriptionRepository([]),
            new FakeAppUserRepository(CreateUser()),
            new FakeBillingService(),
            new FakePostRepository(),
            new FakeTelegramBotGateway(new Dictionary<string, bool>
            {
                ["-1001"] = true,
                ["-1002"] = false
            }),
            new FakeServiceScopeFactory(new FakeServiceProvider()),
            new FakeErrorAlertService(),
            NullLogger<MiniAppChannelService>.Instance);

        var channels = await service.ListAsync(123456789);

        var channel = Assert.Single(channels);
        Assert.Equal("Alpha", channel.ChannelName);
    }

    [Fact]
    public async Task ListAsync_Maps_Channel_And_Subscription_Avatars()
    {
        var user = CreateUser();
        var managedChannel = CreateManagedChannel(user, "Alpha", -1001, "alpha", true);
        var sourceChannelId = Guid.NewGuid();

        var service = new MiniAppChannelService(
            new FakeManagedChannelRepository([managedChannel]),
            new FakeManagedChannelSubscriptionRepository(
            [
                new ManagedChannelSubscription
                {
                    Id = Guid.NewGuid(),
                    ManagedChannelId = managedChannel.Id,
                    ManagedChannel = managedChannel,
                    ChannelId = sourceChannelId,
                    Channel = new TrackedChannel
                    {
                        Id = sourceChannelId,
                        ChannelName = "Source One",
                        UsernameOrInviteLink = "@source_one"
                    },
                    IsActive = true
                }
            ]),
            new FakeAppUserRepository(user),
            new FakeBillingService(),
            new FakePostRepository(),
            new FakeTelegramBotGateway(
                new Dictionary<string, bool>
                {
                    ["-1001"] = true
                },
                new Dictionary<string, string?>
                {
                    ["-1001"] = "data:image/png;base64,alpha",
                    ["@source_one"] = "data:image/png;base64,source"
                }),
            new FakeServiceScopeFactory(new FakeServiceProvider()),
            new FakeErrorAlertService(),
            NullLogger<MiniAppChannelService>.Instance);

        var channels = await service.ListAsync(user.TelegramUserId);

        var channel = Assert.Single(channels);
        Assert.Equal("data:image/png;base64,alpha", channel.AvatarImageUrl);
        var subscription = Assert.Single(channel.Subscriptions);
        Assert.Equal("data:image/png;base64,source", subscription.AvatarImageUrl);
    }

    [Fact]
    public async Task RegisterSharedChannelAsync_Saves_Channel_And_Probes_Posting()
    {
        var requestId = Environment.TickCount;
        var repository = new FakeManagedChannelRepository([]);
        var service = new MiniAppChannelService(
            repository,
            new FakeManagedChannelSubscriptionRepository([]),
            new FakeAppUserRepository(CreateUser()),
            new FakeBillingService(),
            new FakePostRepository(),
            new FakeTelegramBotGateway(new Dictionary<string, bool>
            {
                ["-100200"] = true
            }),
            new FakeServiceScopeFactory(new FakeServiceProvider(repository)),
            new FakeErrorAlertService(),
            NullLogger<MiniAppChannelService>.Instance);

        var result = await service.RegisterSharedChannelAsync(
            123456789,
            new TelegramSharedChatDto(requestId, -100200, "My Private Channel", "private_channel"));

        Assert.True(result.Success);
        var channel = Assert.Single(repository.Items);
        Assert.Equal("My Private Channel", channel.ChannelName);
        Assert.Equal(-100200, channel.TelegramChatId);
        Assert.Equal("private_channel", channel.Username);
        Assert.NotNull(channel.LastVerifiedAtUtc);
    }

    [Fact]
    public async Task RegisterSharedChannelAsync_Adds_Channel_Paused_When_Active_Slots_Are_Full()
    {
        var requestId = Environment.TickCount + 1;
        var user = CreateUser();
        var existingActive = CreateManagedChannel(user, "Alpha", -1001, "alpha", true);
        var repository = new FakeManagedChannelRepository([existingActive]);
        var service = new MiniAppChannelService(
            repository,
            new FakeManagedChannelSubscriptionRepository([]),
            new FakeAppUserRepository(user),
            new FakeBillingService(
                usage: new SubscriptionUsageDto("free", "Free", 1, 0, 1, 1, null, false),
                availablePlans:
                [
                    new SubscriptionPlanDefinitionDto(Guid.NewGuid(), "free", "Free", 1, 1, 0, null, true, true, 0),
                    new SubscriptionPlanDefinitionDto(Guid.NewGuid(), "business-plus-plus", "Business+", 15, 15, 0, null, true, false, 1)
                ]),
            new FakePostRepository(),
            new FakeTelegramBotGateway(new Dictionary<string, bool>
            {
                ["-100200"] = true
            }),
            new FakeServiceScopeFactory(new FakeServiceProvider(repository)),
            new FakeErrorAlertService(),
            NullLogger<MiniAppChannelService>.Instance);

        var result = await service.RegisterSharedChannelAsync(
            user.TelegramUserId,
            new TelegramSharedChatDto(requestId, -100200, "Overflow Channel", "overflow_channel"));

        Assert.True(result.Success);
        var addedChannel = repository.Items.Single(x => x.TelegramChatId == -100200);
        Assert.False(addedChannel.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_Leaves_Channel_Then_Removes_Managed_Channel()
    {
        var user = CreateUser();
        var managedChannel = CreateManagedChannel(user, "Alpha", -1001, "alpha", true);
        var gateway = new FakeTelegramBotGateway(new Dictionary<string, bool> { ["-1001"] = true });
        var repository = new FakeManagedChannelRepository([managedChannel]);
        var service = new MiniAppChannelService(
            repository,
            new FakeManagedChannelSubscriptionRepository([]),
            new FakeAppUserRepository(user),
            new FakeBillingService(),
            new FakePostRepository(),
            gateway,
            new FakeServiceScopeFactory(new FakeServiceProvider()),
            new FakeErrorAlertService(),
            NullLogger<MiniAppChannelService>.Instance);

        var deleted = await service.DeleteAsync(user.TelegramUserId, managedChannel.Id);

        Assert.True(deleted);
        Assert.Empty(repository.Items);
        Assert.Equal("-1001", gateway.LeftChatId);
    }

    [Fact]
    public async Task ListAsync_Includes_Source_Subscriptions_For_Channel()
    {
        var user = CreateUser();
        var managedChannel = CreateManagedChannel(user, "Alpha", -1001, "alpha", true);
        var sourceChannelId = Guid.NewGuid();

        var service = new MiniAppChannelService(
            new FakeManagedChannelRepository([managedChannel]),
            new FakeManagedChannelSubscriptionRepository(
            [
                new ManagedChannelSubscription
                {
                    Id = Guid.NewGuid(),
                    ManagedChannelId = managedChannel.Id,
                    ManagedChannel = managedChannel,
                    ChannelId = sourceChannelId,
                    Channel = new TrackedChannel
                    {
                        Id = sourceChannelId,
                        ChannelName = "Source One",
                        UsernameOrInviteLink = "https://t.me/source_one",
                        LastCollectorError = null
                    },
                    IsActive = true
                }
            ]),
            new FakeAppUserRepository(user),
            new FakeBillingService(),
            new FakePostRepository(),
            new FakeTelegramBotGateway(new Dictionary<string, bool>
            {
                ["-1001"] = true
            }),
            new FakeServiceScopeFactory(new FakeServiceProvider()),
            new FakeErrorAlertService(),
            NullLogger<MiniAppChannelService>.Instance);

        var channels = await service.ListAsync(user.TelegramUserId);

        var channel = Assert.Single(channels);
        var subscription = Assert.Single(channel.Subscriptions);
        Assert.Equal("Source One", subscription.ChannelName);
        Assert.Equal("https://t.me/source_one", subscription.ChannelReference);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_Returns_False_When_No_Free_Managed_Channel_Slots_Are_Available()
    {
        var user = CreateUser();
        var activeChannel = CreateManagedChannel(user, "Alpha", -1001, "alpha", true);
        var pausedChannel = CreateManagedChannel(user, "Beta", -1002, "beta", false);

        var service = new MiniAppChannelService(
            new FakeManagedChannelRepository([activeChannel, pausedChannel]),
            new FakeManagedChannelSubscriptionRepository([]),
            new FakeAppUserRepository(user),
            new FakeBillingService(
                usage: new SubscriptionUsageDto("free", "Free", 1, 0, 1, 1, null, false)),
            new FakePostRepository(),
            new FakeTelegramBotGateway(new Dictionary<string, bool>
            {
                ["-1001"] = true,
                ["-1002"] = true
            }),
            new FakeServiceScopeFactory(new FakeServiceProvider()),
            new FakeErrorAlertService(),
            NullLogger<MiniAppChannelService>.Instance);

        var updated = await service.SetActiveAsync(user.TelegramUserId, pausedChannel.Id, true);

        Assert.False(updated);
        Assert.False(pausedChannel.IsActive);
    }

    private static ManagedChannel CreateManagedChannel(string channelName, long telegramChatId, string? username, bool isActive) =>
        CreateManagedChannel(CreateUser(), channelName, telegramChatId, username, isActive);

    private static AppUser CreateUser() =>
        new()
        {
            Id = Guid.NewGuid(),
            TelegramUserId = 123456789
        };

    private static ManagedChannel CreateManagedChannel(AppUser user, string channelName, long telegramChatId, string? username, bool isActive) =>
        new()
        {
            IsActive = isActive,
            ChannelName = channelName,
            TelegramChatId = telegramChatId,
            Username = username,
            User = user,
            UserId = user.Id
        };

    private sealed class FakeManagedChannelRepository(IReadOnlyList<ManagedChannel> seed) : IManagedChannelRepository
    {
        public List<ManagedChannel> Items { get; } = seed.ToList();

        public Task<ManagedChannel?> GetByIdAsync(Guid managedChannelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedChannel?>(Items.FirstOrDefault(x => x.Id == managedChannelId));

        public Task<ManagedChannel?> GetAsync(Guid userId, Guid managedChannelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedChannel?>(Items.FirstOrDefault(x => x.UserId == userId && x.Id == managedChannelId));

        public Task<ManagedChannel?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedChannel?>(Items.FirstOrDefault(x => x.TelegramChatId == telegramChatId));

        public Task<ManagedChannel?> GetByTelegramChatIdAsync(Guid userId, long telegramChatId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedChannel?>(Items.FirstOrDefault(x => x.UserId == userId && x.TelegramChatId == telegramChatId));

        public Task<IReadOnlyList<ManagedChannel>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagedChannel>>(Items.Where(x => x.User.TelegramUserId == telegramUserId).ToList());

        public Task AddAsync(ManagedChannel managedChannel, CancellationToken cancellationToken = default)
        {
            Items.Add(managedChannel);
            return Task.CompletedTask;
        }

        public void Remove(ManagedChannel managedChannel)
        {
            Items.Remove(managedChannel);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeManagedChannelSubscriptionRepository(IReadOnlyList<ManagedChannelSubscription> seed) : IManagedChannelSubscriptionRepository
    {
        private List<ManagedChannelSubscription> Items { get; } = seed.ToList();

        public Task<ManagedChannelSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedChannelSubscription?>(Items.FirstOrDefault(x => x.Id == subscriptionId));

        public Task<ManagedChannelSubscription?> GetAsync(Guid managedChannelId, Guid channelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedChannelSubscription?>(Items.FirstOrDefault(x => x.ManagedChannelId == managedChannelId && x.ChannelId == channelId));

        public Task<int> CountByManagedChannelIdAsync(Guid managedChannelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Count(x => x.ManagedChannelId == managedChannelId));

        public Task<IReadOnlyList<ManagedChannelSubscription>> GetPageByManagedChannelIdAsync(Guid managedChannelId, int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagedChannelSubscription>>(Items.Where(x => x.ManagedChannelId == managedChannelId).Skip(skip).Take(take).ToList());

        public Task<IReadOnlyList<ManagedChannelSubscription>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagedChannelSubscription>>(Items.Where(x => x.ManagedChannel.User.TelegramUserId == telegramUserId).ToList());

        public Task<IReadOnlyList<ManagedChannelSubscription>> GetByChannelIdAsync(Guid channelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagedChannelSubscription>>(Items.Where(x => x.ChannelId == channelId).ToList());

        public Task<IReadOnlyList<ManagedChannelSubscription>> GetByManagedChannelIdAsync(Guid managedChannelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagedChannelSubscription>>(Items.Where(x => x.ManagedChannelId == managedChannelId).ToList());

        public Task<IReadOnlyList<ManagedChannelSubscription>> GetActiveForDeliveryAsync(int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagedChannelSubscription>>(Items.Where(x => x.IsActive).Take(take).ToList());

        public Task AddAsync(ManagedChannelSubscription subscription, CancellationToken cancellationToken = default)
        {
            Items.Add(subscription);
            return Task.CompletedTask;
        }

        public void Remove(ManagedChannelSubscription subscription)
        {
            Items.Remove(subscription);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAppUserRepository(AppUser user) : IAppUserRepository
    {
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AppUser?>(user.Id == userId ? user : null);

        public Task<AppUser?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AppUser?>(user.TelegramUserId == telegramUserId ? user : null);

        public Task<IReadOnlyList<AppUser>> ListForAdminAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppUser>>([user]);

        public Task AddAsync(AppUser user, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBillingService(
        SubscriptionUsageDto? usage = null,
        IReadOnlyList<SubscriptionPlanDefinitionDto>? availablePlans = null) : IBillingService
    {
        public Task<SubscriptionUsageDto> GetSubscriptionUsageAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(usage ?? new SubscriptionUsageDto("free", "Free", 1, 0, 1, 0, null, false));

        public Task<ChannelTrackingResultDto> CanAddChannelAsync(long telegramUserId, Guid channelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelTrackingResultDto(true, string.Empty));

        public Task<ChannelTrackingResultDto> CanAddManagedChannelAsync(long telegramUserId, long telegramChatId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelTrackingResultDto(true, string.Empty));

        public Task<IReadOnlyList<SubscriptionPlanDefinitionDto>> ListAvailablePlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDefinitionDto>>(availablePlans ?? []);

        public Task<IReadOnlyList<DonationOptionDto>> ListAvailableDonationOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DonationOptionDto>>([]);

        public Task<BillingInvoiceResultDto> CreatePlanInvoiceAsync(BillingInvoiceRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BillingInvoiceResultDto(false, "n/a"));

        public Task<BillingInvoiceResultDto> CreateDonationInvoiceAsync(BillingInvoiceRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BillingInvoiceResultDto(false, "n/a"));

        public Task<PreCheckoutDecisionDto> ValidatePreCheckoutAsync(TelegramPreCheckoutQueryDto query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PreCheckoutDecisionDto(true));

        public Task<PaymentProcessingResultDto> ProcessSuccessfulPaymentAsync(long telegramUserId, TelegramSuccessfulPaymentDto payment, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentProcessingResultDto(true, "ok"));
    }

    private sealed class FakeTelegramBotGateway(
        IReadOnlyDictionary<string, bool> adminChannels,
        IReadOnlyDictionary<string, string?>? avatars = null) : ITelegramBotGateway
    {
        public string? LeftChatId { get; private set; }

        public Task<IReadOnlyList<TelegramBotUpdateDto>> GetUpdatesAsync(long offset, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TelegramBotUpdateDto>>([]);

        public Task<TelegramBotApiResultDto> SendMessageAsync(TelegramBotOutboundMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> SendInvoiceAsync(TelegramBotInvoiceDto invoice, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> SendPhotoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> SendVideoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> SendAudioAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> SendVoiceAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> SendDocumentAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> SendAnimationAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> SendStickerAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> SendVideoNoteAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto?> SendMediaGroupAsync(TelegramBotMediaGroupMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult<TelegramBotApiResultDto?>(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> AnswerPreCheckoutQueryAsync(string preCheckoutQueryId, bool ok, string? errorMessage, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<bool> IsBotAdministratorAsync(string telegramChannelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(adminChannels.TryGetValue(telegramChannelId, out var isAdmin) && isAdmin);

        public Task<TelegramBotApiResultDto> LeaveChatAsync(string telegramChannelId, CancellationToken cancellationToken = default)
        {
            LeftChatId = telegramChannelId;
            return Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));
        }

        public Task<TelegramBotApiResultDto> SetChatMenuButtonAsync(string text, string webAppUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<string?> GetChatProfileImageDataUrlAsync(string telegramChatReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                avatars is not null && avatars.TryGetValue(telegramChatReference, out var avatar)
                    ? avatar
                    : null);
    }

    private sealed class FakePostRepository : IPostRepository
    {
        public Task<TelegramPost?> GetByChannelAndMessageIdAsync(Guid channelId, long telegramMessageId, CancellationToken cancellationToken = default) => Task.FromResult<TelegramPost?>(null);
        public Task<IReadOnlyDictionary<long, TelegramPost>> GetByChannelAndMessageIdsAsync(Guid channelId, IReadOnlyCollection<long> telegramMessageIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<long, TelegramPost>>(new Dictionary<long, TelegramPost>());
        public Task<TelegramPost?> GetByIdAsync(Guid postId, CancellationToken cancellationToken = default) => Task.FromResult<TelegramPost?>(null);
        public Task<IReadOnlyList<TelegramPost>> GetFeedForUserAsync(long telegramUserId, int take, int skip, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TelegramPost>>([]);
        public Task<IReadOnlyList<TelegramPost>> GetUndeliveredForChannelAsync(Guid channelId, long? lastDeliveredTelegramMessageId, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TelegramPost>>([]);
        public Task<IReadOnlyList<TelegramPost>> GetByChannelAndMediaGroupIdAsync(Guid channelId, string mediaGroupId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TelegramPost>>([]);
        public Task<long?> GetLatestTelegramMessageIdForChannelAsync(Guid channelId, CancellationToken cancellationToken = default) => Task.FromResult<long?>(999);
        public Task<IReadOnlyList<TelegramPost>> GetPendingEmbeddingsBatchAsync(DateTimeOffset notOlderThanUtc, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TelegramPost>>([]);
        public Task<IReadOnlyList<TelegramPost>> GetExpiredPendingEmbeddingsAsync(DateTimeOffset olderThanUtc, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TelegramPost>>([]);
        public Task<int> CountByEmbeddingStatusAsync(TelegramPostAggregator.Domain.Enums.EmbeddingStatus status, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task AddAsync(TelegramPost post, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeServiceScopeFactory(IServiceProvider serviceProvider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FakeServiceScope(serviceProvider);
    }

    private sealed class FakeServiceScope(IServiceProvider serviceProvider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public void Dispose()
        {
        }
    }

    private sealed class FakeServiceProvider(IManagedChannelRepository? managedChannelRepository = null) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (managedChannelRepository is not null && serviceType == typeof(IManagedChannelRepository))
            {
                return managedChannelRepository;
            }

            return null;
        }
    }

    private sealed class FakeErrorAlertService : IErrorAlertService
    {
        public Task SendAsync(string title, string message, Exception? exception = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
