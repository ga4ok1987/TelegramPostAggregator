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
}
