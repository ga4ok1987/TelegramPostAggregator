using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Services;
using TelegramPostAggregator.Application.Services.Bot;
using Xunit;

namespace TelegramPostAggregator.Application.Tests;

public sealed class BotUpdateProcessorTests
{
    [Fact]
    public async Task ProcessAsync_StartCommand_UsesPreferredLanguageMenu()
    {
        var userService = new FakeUserService("es");
        var trackingService = new FakeChannelTrackingService
        {
            SetSubscriptionsActiveResult = 2
        };
        var processor = CreateProcessor(userService, trackingService);

        var result = await processor.ProcessAsync(CreateUpdate("/start", "es"));

        Assert.True(result.Success);
        Assert.Contains("Se reanudaron 2 suscripciones", result.Message);
        Assert.NotNull(result.ReplyMarkup);
        Assert.Contains(result.ReplyMarkup!.Buttons.SelectMany(x => x), button => button.Text == "Iniciar");
        Assert.Contains(result.ReplyMarkup.Buttons.SelectMany(x => x), button => button.Text == "🇪🇸 Idioma");
    }

    [Fact]
    public async Task ProcessAsync_LanguageSelectionCallback_UpdatesPreferredLanguage()
    {
        var userService = new FakeUserService("uk");
        var processor = CreateProcessor(userService, new FakeChannelTrackingService());

        var result = await processor.ProcessAsync(CreateUpdate(null, "uk", "language:set:de"));

        Assert.True(result.Success);
        Assert.Equal("de", userService.CurrentLanguageCode);
        Assert.Contains("Sprache", result.Message);
        Assert.NotNull(result.ReplyMarkup);
        Assert.Contains(result.ReplyMarkup!.Buttons.SelectMany(x => x), button => button.Text == "Starten");
        Assert.Contains(result.ReplyMarkup.Buttons.SelectMany(x => x), button => button.Text == "🇩🇪 Sprache");
    }

    [Fact]
    public async Task ProcessAsync_LanguageButtonText_OpensLanguageMenu()
    {
        var userService = new FakeUserService("pt");
        var processor = CreateProcessor(userService, new FakeChannelTrackingService());

        var result = await processor.ProcessAsync(CreateUpdate("🇵🇹 Idioma", "pt"));

        Assert.True(result.Success);
        Assert.Contains("Escolha seu idioma", result.Message);
        Assert.NotNull(result.ReplyMarkup);
        Assert.True(result.ReplyMarkup!.IsInline);
        Assert.Contains(result.ReplyMarkup.Buttons.SelectMany(x => x), button => button.CallbackData == "language:set:uk");
    }

    [Fact]
    public async Task ProcessAsync_PlainLanguageLabel_DoesNotBecomeSubscription()
    {
        var userService = new FakeUserService("uk");
        var trackingService = new FakeChannelTrackingService();
        var processor = CreateProcessor(userService, trackingService);

        var result = await processor.ProcessAsync(CreateUpdate("Мова", "uk"));

        Assert.True(result.Success);
        Assert.Equal(0, trackingService.AddTrackedChannelCalls);
        Assert.Contains("Оберіть мову", result.Message);
        Assert.NotNull(result.ReplyMarkup);
        Assert.True(result.ReplyMarkup!.IsInline);
    }

    [Fact]
    public async Task ProcessAsync_PlainLanguageName_ChangesLanguageInsteadOfAddingSubscription()
    {
        var userService = new FakeUserService("uk");
        var trackingService = new FakeChannelTrackingService();
        var processor = CreateProcessor(userService, trackingService);

        var result = await processor.ProcessAsync(CreateUpdate("English", "uk"));

        Assert.True(result.Success);
        Assert.Equal(0, trackingService.AddTrackedChannelCalls);
        Assert.Equal("en", userService.CurrentLanguageCode);
        Assert.Contains("Language updated", result.Message);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_FiltersReservedUiEntries()
    {
        var service = new ChannelTrackingService(
            new FakeUserService("uk"),
            new FakeTrackedChannelRepository(),
            new FakeSubscriptionRepository(
                [
                    new SubscriptionDto(Guid.NewGuid(), "Real Channel", "https://t.me/real_channel", "Active", true),
                    new SubscriptionDto(Guid.NewGuid(), "🇬🇧 english", "🇬🇧 English", "Pending", true)
                ]),
            new FakeCollectorAccountRepository(),
            new FakePostRepository(),
            new FakeChannelKeyNormalizer(),
            new BotLocalizationCatalog());

        var result = await service.ListSubscriptionsAsync(123);

        Assert.Single(result);
        Assert.Equal("Real Channel", result[0].ChannelName);
    }

    [Fact]
    public async Task ProcessAsync_DeleteOneCallback_ReturnsConfirmationForMatchingSubscription()
    {
        var channelId = Guid.NewGuid();
        var userService = new FakeUserService("en");
        var trackingService = new FakeChannelTrackingService
        {
            Subscriptions =
            [
                new SubscriptionDto(channelId, "Test Channel", "@test_channel", "Active", true)
            ]
        };
        var processor = CreateProcessor(userService, trackingService);

        var result = await processor.ProcessAsync(CreateUpdate(null, "en", $"delete_one:{channelId}"));

        Assert.True(result.Success);
        Assert.Contains("Delete subscription Test Channel?", result.Message);
        Assert.Equal("Confirm subscription deletion.", result.CallbackNotification);
        Assert.NotNull(result.ReplyMarkup);
        Assert.True(result.ReplyMarkup!.IsInline);
        Assert.Contains(result.ReplyMarkup.Buttons.SelectMany(x => x), button => button.CallbackData == $"delete_one:confirm:{channelId}");
    }

    [Fact]
    public async Task ProcessAsync_ListCommand_WithoutSubscriptions_ReturnsLocalizedEmptyState()
    {
        var userService = new FakeUserService("fr");
        var processor = CreateProcessor(userService, new FakeChannelTrackingService());

        var result = await processor.ProcessAsync(CreateUpdate("/list", "fr"));

        Assert.True(result.Success);
        Assert.Contains("Aucun abonnement", result.Message);
        Assert.NotNull(result.ReplyMarkup);
        Assert.False(result.ReplyMarkup!.IsInline);
    }

    [Fact]
    public async Task ProcessAsync_StopMenuCallback_ReturnsPauseNotice()
    {
        var processor = CreateProcessor(new FakeUserService("uk"), new FakeChannelTrackingService());

        var result = await processor.ProcessAsync(CreateUpdate(null, "uk", "menu:stop"));

        Assert.True(result.Success);
        Assert.Equal("Підтвердіть зупинку або скасуйте.", result.CallbackNotification);
    }

    [Fact]
    public void BuildSubscriptionsListMessage_UsesReadableStatusIcons()
    {
        var messages = new BotMessageCatalog(new BotLocalizationCatalog());
        var subscriptions = new[]
        {
            new SubscriptionDto(Guid.NewGuid(), "Active Channel", "@active", "Active", true),
            new SubscriptionDto(Guid.NewGuid(), "Paused Channel", "@paused", "Paused", false)
        };

        var result = messages.BuildSubscriptionsListMessage(subscriptions, "en");

        Assert.Contains("1. 🟢 Active Channel", result);
        Assert.Contains("2. ⏸ Paused Channel", result);
    }

    private static BotUpdateProcessor CreateProcessor(IUserService userService, IChannelTrackingService trackingService)
    {
        var localizationCatalog = new BotLocalizationCatalog();
        return new BotUpdateProcessor(
            userService,
            trackingService,
            localizationCatalog,
            new BotMenuFactory(localizationCatalog),
            new BotMessageCatalog(localizationCatalog));
    }

    private static TelegramBotUpdateDto CreateUpdate(string? text, string languageCode, string? callbackData = null) =>
        new(
            1,
            new BotUserSnapshotDto(123, "tester", "Test User", languageCode),
            text,
            callbackData is null ? null : "callback-1",
            callbackData,
            123,
            DateTimeOffset.UtcNow);

    private sealed class FakeUserService(string initialLanguageCode) : IUserService
    {
        public string CurrentLanguageCode { get; private set; } = initialLanguageCode;

        public Task<UserDto> UpsertTelegramUserAsync(BotUserSnapshotDto snapshot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserDto(Guid.NewGuid(), snapshot.TelegramUserId, snapshot.TelegramUsername, snapshot.DisplayName, CurrentLanguageCode));

        public Task<UserDto> SetPreferredLanguageAsync(long telegramUserId, string languageCode, CancellationToken cancellationToken = default)
        {
            CurrentLanguageCode = languageCode;
            return Task.FromResult(new UserDto(Guid.NewGuid(), telegramUserId, "tester", "Test User", CurrentLanguageCode));
        }
    }

    private sealed class FakeChannelTrackingService : IChannelTrackingService
    {
        public int SetSubscriptionsActiveResult { get; set; }
        public int AddTrackedChannelCalls { get; private set; }

        public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = [];

        public Task<ChannelDto> AddTrackedChannelAsync(AddTrackedChannelDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelDto(
                AddTrackedChannel(request),
                request.ChannelReference,
                request.ChannelReference,
                "Pending",
                null,
                null));

        private Guid AddTrackedChannel(AddTrackedChannelDto request)
        {
            AddTrackedChannelCalls++;
            return Guid.NewGuid();
        }

        public Task RemoveTrackedChannelAsync(RemoveTrackedChannelDto request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> RemoveTrackedChannelByIdAsync(RemoveTrackedChannelByIdDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Subscriptions.Any(x => x.ChannelId == request.ChannelId));

        public Task<int> RemoveAllTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Subscriptions.Count);

        public Task<int> SetSubscriptionsActiveAsync(long telegramUserId, bool isActive, CancellationToken cancellationToken = default) =>
            Task.FromResult(SetSubscriptionsActiveResult);

        public Task<IReadOnlyList<ChannelDto>> ListTrackedChannelsAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChannelDto>>([]);

        public Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Subscriptions);
    }

    private sealed class FakeTrackedChannelRepository : Abstractions.Repositories.ITrackedChannelRepository
    {
        public Task AddAsync(Domain.Entities.TrackedChannel channel, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Domain.Entities.TrackedChannel?> GetByNormalizedKeyAsync(string normalizedKey, CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.TrackedChannel?>(null);
        public Task<Domain.Entities.TrackedChannel?> GetByTelegramChannelIdAsync(string telegramChannelId, CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.TrackedChannel?>(null);
        public Task<Domain.Entities.TrackedChannel?> GetWithAssignmentsAsync(Guid channelId, CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.TrackedChannel?>(null);
        public Task<IReadOnlyList<Domain.Entities.TrackedChannel>> GetChannelsForUserAsync(long telegramUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Domain.Entities.TrackedChannel>>([]);
        public Task<IReadOnlyList<Domain.Entities.TrackedChannel>> GetChannelsByStatusAsync(Domain.Enums.ChannelTrackingStatus status, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Domain.Entities.TrackedChannel>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSubscriptionRepository(IReadOnlyList<SubscriptionDto> subscriptions) : Abstractions.Repositories.ISubscriptionRepository
    {
        public Task<Domain.Entities.UserChannelSubscription?> GetAsync(Guid userId, Guid channelId, CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.UserChannelSubscription?>(null);
        public Task<IReadOnlyList<Domain.Entities.UserChannelSubscription>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Domain.Entities.UserChannelSubscription>>(subscriptions.Select(ToEntity).ToList());
        public Task<IReadOnlyList<Domain.Entities.UserChannelSubscription>> GetActiveByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Domain.Entities.UserChannelSubscription>>([]);
        public Task<IReadOnlyList<Domain.Entities.UserChannelSubscription>> GetActiveForDeliveryAsync(int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Domain.Entities.UserChannelSubscription>>([]);
        public Task AddAsync(Domain.Entities.UserChannelSubscription subscription, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(Domain.Entities.UserChannelSubscription subscription) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static Domain.Entities.UserChannelSubscription ToEntity(SubscriptionDto dto) =>
            new()
            {
                ChannelId = dto.ChannelId,
                IsActive = dto.IsActive,
                Channel = new Domain.Entities.TrackedChannel
                {
                    Id = dto.ChannelId,
                    ChannelName = dto.ChannelName,
                    UsernameOrInviteLink = dto.ChannelReference,
                    NormalizedChannelKey = dto.ChannelName.ToLowerInvariant(),
                    Status = Domain.Enums.ChannelTrackingStatus.Active
                },
                User = new Domain.Entities.AppUser
                {
                    TelegramUserId = 123
                }
            };
    }

    private sealed class FakeCollectorAccountRepository : Abstractions.Repositories.ICollectorAccountRepository
    {
        public Task<Domain.Entities.CollectorAccount?> GetPrimaryAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.CollectorAccount?>(null);
        public Task<Domain.Entities.CollectorAccount?> GetByIdAsync(Guid collectorAccountId, CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.CollectorAccount?>(null);
        public Task<IReadOnlyList<Domain.Entities.ChannelCollectorAssignment>> GetPendingAssignmentsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Domain.Entities.ChannelCollectorAssignment>>([]);
        public Task<IReadOnlyList<Domain.Entities.ChannelCollectorAssignment>> GetAssignmentsForSynchronizationAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Domain.Entities.ChannelCollectorAssignment>>([]);
        public Task AddAsync(Domain.Entities.CollectorAccount collectorAccount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddAssignmentAsync(Domain.Entities.ChannelCollectorAssignment assignment, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakePostRepository : Abstractions.Repositories.IPostRepository
    {
        public Task<Domain.Entities.TelegramPost?> GetByChannelAndMessageIdAsync(Guid channelId, long telegramMessageId, CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.TelegramPost?>(null);
        public Task<IReadOnlyDictionary<long, Domain.Entities.TelegramPost>> GetByChannelAndMessageIdsAsync(Guid channelId, IReadOnlyCollection<long> telegramMessageIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<long, Domain.Entities.TelegramPost>>(new Dictionary<long, Domain.Entities.TelegramPost>());
        public Task<Domain.Entities.TelegramPost?> GetByIdAsync(Guid postId, CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.TelegramPost?>(null);
        public Task<IReadOnlyList<Domain.Entities.TelegramPost>> GetFeedForUserAsync(long telegramUserId, int take, int skip, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Domain.Entities.TelegramPost>>([]);
        public Task<IReadOnlyList<Domain.Entities.TelegramPost>> GetUndeliveredForChannelAsync(Guid channelId, long? lastDeliveredTelegramMessageId, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Domain.Entities.TelegramPost>>([]);
        public Task<IReadOnlyList<Domain.Entities.TelegramPost>> GetByChannelAndMediaGroupIdAsync(Guid channelId, string mediaGroupId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Domain.Entities.TelegramPost>>([]);
        public Task<long?> GetLatestTelegramMessageIdForChannelAsync(Guid channelId, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task AddAsync(Domain.Entities.TelegramPost post, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeChannelKeyNormalizer : IChannelKeyNormalizer
    {
        public string Normalize(string input) => input.Trim().ToLowerInvariant();
    }
}
