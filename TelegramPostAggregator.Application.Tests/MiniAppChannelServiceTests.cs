using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Services;
using TelegramPostAggregator.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TelegramPostAggregator.Application.Tests;

public sealed class MiniAppChannelServiceTests
{
    [Fact]
    public async Task ListAsync_Returns_Only_Channels_Where_Bot_Is_Admin()
    {
        var service = new MiniAppChannelService(
            new FakeManagedChannelRepository(
            [
                CreateManagedChannel("Alpha", -1001, "alpha", true),
                CreateManagedChannel("Beta", -1002, "beta", true)
            ]),
            new FakeAppUserRepository(CreateUser()),
            new FakeTelegramBotGateway(new Dictionary<string, bool>
            {
                ["-1001"] = true,
                ["-1002"] = false
            }),
            NullLogger<MiniAppChannelService>.Instance);

        var channels = await service.ListAsync(123456789);

        var channel = Assert.Single(channels);
        Assert.Equal("Alpha", channel.ChannelName);
    }

    [Fact]
    public async Task RegisterSharedChannelAsync_Saves_Channel_And_Probes_Posting()
    {
        var repository = new FakeManagedChannelRepository([]);
        var service = new MiniAppChannelService(
            repository,
            new FakeAppUserRepository(CreateUser()),
            new FakeTelegramBotGateway(new Dictionary<string, bool>
            {
                ["-100200"] = true
            }),
            NullLogger<MiniAppChannelService>.Instance);

        var result = await service.RegisterSharedChannelAsync(
            123456789,
            new TelegramSharedChatDto(1001, -100200, "My Private Channel", "private_channel"));

        Assert.True(result.Success);
        var channel = Assert.Single(repository.Items);
        Assert.Equal("My Private Channel", channel.ChannelName);
        Assert.Equal(-100200, channel.TelegramChatId);
        Assert.Equal("private_channel", channel.Username);
        Assert.NotNull(channel.LastWriteSucceededAtUtc);
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

        public Task<ManagedChannel?> GetAsync(Guid userId, Guid managedChannelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedChannel?>(Items.FirstOrDefault(x => x.UserId == userId && x.Id == managedChannelId));

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

    private sealed class FakeAppUserRepository(AppUser user) : IAppUserRepository
    {
        public Task<AppUser?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AppUser?>(user.TelegramUserId == telegramUserId ? user : null);

        public Task AddAsync(AppUser user, CancellationToken cancellationToken = default) => Task.CompletedTask;

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

        public Task<TelegramBotApiResultDto> SetChatMenuButtonAsync(string text, string webAppUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramBotApiResultDto(true, System.Net.HttpStatusCode.OK, null));
    }
}
