using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Services;
using TelegramPostAggregator.Application.Services.Bot;
using Xunit;

namespace TelegramPostAggregator.Application.Tests;

public sealed class BotUpdateProcessorTests
{
    [Fact]
    public async Task ProcessAsync_StartCommand_ResumesSubscriptionsAndReturnsMainMenu()
    {
        var userService = new FakeUserService();
        var trackingService = new FakeChannelTrackingService
        {
            SetSubscriptionsActiveResult = 2
        };
        var processor = CreateProcessor(userService, trackingService);

        var result = await processor.ProcessAsync(CreateUpdate("/start"));

        Assert.True(result.Success);
        Assert.Contains("Поновив 2 підписок", result.Message);
        Assert.NotNull(result.ReplyMarkup);
        Assert.False(result.ReplyMarkup!.IsInline);
        Assert.Contains(result.ReplyMarkup.Buttons.SelectMany(x => x), button => button.Text == BotMenuFactory.StartLabel);
    }

    [Fact]
    public async Task ProcessAsync_DeleteOneCallback_ReturnsConfirmationForMatchingSubscription()
    {
        var channelId = Guid.NewGuid();
        var userService = new FakeUserService();
        var trackingService = new FakeChannelTrackingService
        {
            Subscriptions =
            [
                new SubscriptionDto(channelId, "Test Channel", "@test_channel", "Active", true)
            ]
        };
        var processor = CreateProcessor(userService, trackingService);

        var result = await processor.ProcessAsync(CreateUpdate(null, $"delete_one:{channelId}"));

        Assert.True(result.Success);
        Assert.Contains("Видалити підписку Test Channel?", result.Message);
        Assert.Equal("Підтвердіть видалення підписки.", result.CallbackNotification);
        Assert.NotNull(result.ReplyMarkup);
        Assert.True(result.ReplyMarkup!.IsInline);
        Assert.Contains(result.ReplyMarkup.Buttons.SelectMany(x => x), button => button.CallbackData == $"delete_one:confirm:{channelId}");
    }

    [Fact]
    public async Task ProcessAsync_ListCommand_WithoutSubscriptions_ReturnsEmptyState()
    {
        var userService = new FakeUserService();
        var trackingService = new FakeChannelTrackingService();
        var processor = CreateProcessor(userService, trackingService);

        var result = await processor.ProcessAsync(CreateUpdate("/list"));

        Assert.True(result.Success);
        Assert.Equal("Підписок ще немає. Надішліть посилання на канал, щоб додати його.", result.Message);
        Assert.NotNull(result.ReplyMarkup);
        Assert.False(result.ReplyMarkup!.IsInline);
    }

    [Fact]
    public async Task ProcessAsync_StopMenuCallback_ReturnsPauseNotice()
    {
        var processor = CreateProcessor(new FakeUserService(), new FakeChannelTrackingService());

        var result = await processor.ProcessAsync(CreateUpdate(null, "menu:stop"));

        Assert.True(result.Success);
        Assert.Equal("Підтвердіть зупинку або скасуйте.", result.CallbackNotification);
    }

    [Fact]
    public void BuildSubscriptionsListMessage_UsesReadableStatusIcons()
    {
        var messages = new BotMessageCatalog();
        var subscriptions = new[]
        {
            new SubscriptionDto(Guid.NewGuid(), "Active Channel", "@active", "Active", true),
            new SubscriptionDto(Guid.NewGuid(), "Paused Channel", "@paused", "Paused", false)
        };

        var result = messages.BuildSubscriptionsListMessage(subscriptions);

        Assert.Contains("1. 🟢 Active Channel", result);
        Assert.Contains("2. ⏸ Paused Channel", result);
    }

    private static BotUpdateProcessor CreateProcessor(IUserService userService, IChannelTrackingService trackingService) =>
        new(userService, trackingService, new BotMenuFactory(), new BotMessageCatalog());

    private static TelegramBotUpdateDto CreateUpdate(string? text, string? callbackData = null) =>
        new(
            1,
            new BotUserSnapshotDto(123, "tester", "Test User", "uk"),
            text,
            callbackData is null ? null : "callback-1",
            callbackData,
            123,
            DateTimeOffset.UtcNow);

    private sealed class FakeUserService : IUserService
    {
        public Task<UserDto> UpsertTelegramUserAsync(BotUserSnapshotDto snapshot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserDto(Guid.NewGuid(), snapshot.TelegramUserId, snapshot.TelegramUsername, snapshot.DisplayName, snapshot.LanguageCode ?? "uk"));
    }

    private sealed class FakeChannelTrackingService : IChannelTrackingService
    {
        public int SetSubscriptionsActiveResult { get; set; }

        public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = [];

        public Task<ChannelDto> AddTrackedChannelAsync(AddTrackedChannelDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelDto(Guid.NewGuid(), request.ChannelReference, request.ChannelReference, "Pending", null, null));

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
