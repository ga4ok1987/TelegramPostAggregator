using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Services;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using Xunit;

namespace TelegramPostAggregator.Application.Tests;

public sealed class MiniAppChannelServiceTests
{
    [Fact]
    public async Task ListAsync_Returns_Only_Channels_Where_Bot_Is_Admin()
    {
        var allowedChannelId = Guid.NewGuid();
        var deniedChannelId = Guid.NewGuid();
        var service = new MiniAppChannelService(
            new FakeSubscriptionRepository(
            [
                CreateSubscription(allowedChannelId, "Alpha", "-1001", "@alpha", true),
                CreateSubscription(deniedChannelId, "Beta", "-1002", "@beta", true)
            ]),
            new FakeTelegramBotGateway(new Dictionary<string, bool>
            {
                ["-1001"] = true,
                ["-1002"] = false
            }));

        var channels = await service.ListAsync(123456789);

        var channel = Assert.Single(channels);
        Assert.Equal(allowedChannelId, channel.ChannelId);
        Assert.Equal("Alpha", channel.ChannelName);
    }

    [Fact]
    public async Task ListAsync_Ignores_Reserved_Entries_Before_Admin_Check()
    {
        var service = new MiniAppChannelService(
            new FakeSubscriptionRepository(
            [
                CreateSubscription(Guid.NewGuid(), "Language", "-1001", "Language", true),
                CreateSubscription(Guid.NewGuid(), "Real Channel", "-1002", "@real", true)
            ]),
            new FakeTelegramBotGateway(new Dictionary<string, bool>
            {
                ["-1002"] = true
            }));

        var channels = await service.ListAsync(123456789);

        var channel = Assert.Single(channels);
        Assert.Equal("Real Channel", channel.ChannelName);
    }

    private static UserChannelSubscription CreateSubscription(Guid channelId, string channelName, string telegramChannelId, string reference, bool isActive) =>
        new()
        {
            ChannelId = channelId,
            IsActive = isActive,
            Channel = new TrackedChannel
            {
                Id = channelId,
                TelegramChannelId = telegramChannelId,
                ChannelName = channelName,
                UsernameOrInviteLink = reference,
                Status = ChannelTrackingStatus.Active
            },
            User = new AppUser
            {
                Id = Guid.NewGuid(),
                TelegramUserId = 123456789
            }
        };

    private sealed class FakeSubscriptionRepository(IReadOnlyList<UserChannelSubscription> subscriptions) : ISubscriptionRepository
    {
        public Task<UserChannelSubscription?> GetAsync(Guid userId, Guid channelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserChannelSubscription?>(subscriptions.FirstOrDefault(x => x.User.Id == userId && x.ChannelId == channelId));

        public Task<IReadOnlyList<UserChannelSubscription>> GetByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserChannelSubscription>>(subscriptions.Where(x => x.User.TelegramUserId == telegramUserId).ToList());

        public Task<IReadOnlyList<UserChannelSubscription>> GetActiveByUserTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserChannelSubscription>>(subscriptions.Where(x => x.User.TelegramUserId == telegramUserId && x.IsActive).ToList());

        public Task<IReadOnlyList<UserChannelSubscription>> GetActiveForDeliveryAsync(int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserChannelSubscription>>([]);

        public Task AddAsync(UserChannelSubscription subscription, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(UserChannelSubscription subscription)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTelegramBotGateway(IReadOnlyDictionary<string, bool> adminChannels) : ITelegramBotGateway
    {
        public Task<IReadOnlyList<TelegramBotUpdateDto>> GetUpdatesAsync(long offset, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TelegramBotUpdateDto>>([]);

        public Task<TelegramBotApiResultDto> SendMessageAsync(TelegramBotOutboundMessageDto message, CancellationToken cancellationToken = default) =>
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

        public Task<TelegramBotApiResultDto> SendVideoNoteAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto?> SendMediaGroupAsync(TelegramBotMediaGroupMessageDto message, CancellationToken cancellationToken = default) =>
            Task.FromResult<TelegramBotApiResultDto?>(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<TelegramBotApiResultDto> AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));

        public Task<bool> IsBotAdministratorAsync(string telegramChannelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(adminChannels.TryGetValue(telegramChannelId, out var isAdmin) && isAdmin);
    }
}
