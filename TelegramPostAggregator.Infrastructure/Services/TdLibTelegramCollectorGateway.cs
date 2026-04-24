using TdLib;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Infrastructure.Models;
using TelegramPostAggregator.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TdLibTelegramCollectorGateway(
    IOptions<TdLibOptions> options,
    TdLibCollectorClientManager clientManager,
    IErrorAlertService errorAlertService,
    ILogger<TdLibTelegramCollectorGateway> logger) : ITelegramCollectorGateway
{
    private const int ChatHistoryPageSize = 100;
    private const int MaxHistoryPagesPerSync = 10;

    public Task<CollectorAuthStatusDto> GetAuthorizationStatusAsync(CollectorAccount collectorAccount, CancellationToken cancellationToken = default) =>
        clientManager.GetStatusAsync(collectorAccount, cancellationToken);

    public Task<CollectorAuthStatusDto> InitializeAuthenticationAsync(CollectorAccount collectorAccount, CancellationToken cancellationToken = default) =>
        clientManager.InitializeAsync(collectorAccount, cancellationToken);

    public Task<CollectorAuthStatusDto> SubmitAuthenticationCodeAsync(CollectorAccount collectorAccount, string code, CancellationToken cancellationToken = default) =>
        clientManager.SubmitCodeAsync(collectorAccount, code, cancellationToken);

    public Task<CollectorAuthStatusDto> SubmitAuthenticationPasswordAsync(CollectorAccount collectorAccount, string password, CancellationToken cancellationToken = default) =>
        clientManager.SubmitPasswordAsync(collectorAccount, password, cancellationToken);

    public Task<CollectorJoinResultDto> EnsureJoinedAsync(CollectorAccount collectorAccount, TrackedChannel channel, CancellationToken cancellationToken = default)
    {
        if (options.Value.UseSimulation)
        {
            logger.LogInformation("TDLib simulation mode is enabled. Pretending to join {ChannelKey}", channel.NormalizedChannelKey);
            return Task.FromResult(new CollectorJoinResultDto(true, channel.NormalizedChannelKey, channel.ChannelName, null));
        }

        return EnsureJoinedLiveAsync(collectorAccount, channel, cancellationToken);
    }

    public Task<IReadOnlyList<CollectedPostDto>> GetRecentPostsAsync(
        CollectorAccount collectorAccount,
        TrackedChannel channel,
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken = default)
    {
        if (options.Value.UseSimulation)
        {
            IReadOnlyList<CollectedPostDto> posts = Array.Empty<CollectedPostDto>();
            return Task.FromResult(posts);
        }

        return GetRecentPostsLiveAsync(collectorAccount, channel, sinceUtc, cancellationToken);
    }

    private async Task<CollectorJoinResultDto> EnsureJoinedLiveAsync(CollectorAccount collectorAccount, TrackedChannel channel, CancellationToken cancellationToken)
    {
        try
        {
            var client = await clientManager.GetAuthorizedClientAsync(collectorAccount, cancellationToken);
            var chat = await ResolveChatAsync(client, channel, cancellationToken);

            await client.ExecuteAsync(new TdApi.JoinChat { ChatId = chat.Id });
            await client.ExecuteAsync(new TdApi.OpenChat { ChatId = chat.Id });

            return new CollectorJoinResultDto(true, chat.Id.ToString(), chat.Title, null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to join channel {ChannelReference}", channel.UsernameOrInviteLink);
            await errorAlertService.SendAsync(
                "Collector failed to join channel",
                $"Channel: {channel.ChannelName}\nReference: {channel.UsernameOrInviteLink}",
                exception,
                cancellationToken);
            return new CollectorJoinResultDto(false, null, null, exception.Message);
        }
    }

    private async Task<IReadOnlyList<CollectedPostDto>> GetRecentPostsLiveAsync(
        CollectorAccount collectorAccount,
        TrackedChannel channel,
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await clientManager.GetAuthorizedClientAsync(collectorAccount, cancellationToken);
            var chat = await ResolveChatAsync(client, channel, cancellationToken);
            await client.ExecuteAsync(new TdApi.OpenChat { ChatId = chat.Id });

            var posts = new List<CollectedPostDto>();
            var fromMessageId = 0L;
            for (var page = 0; page < MaxHistoryPagesPerSync; page++)
            {
                var history = await client.ExecuteAsync(new TdApi.GetChatHistory
                {
                    ChatId = chat.Id,
                    FromMessageId = fromMessageId,
                    Offset = 0,
                    Limit = ChatHistoryPageSize,
                    OnlyLocal = false
                });

                var messages = history.Messages_;
                if (messages.Length == 0)
                {
                    break;
                }

                var reachedSyncedHistory = false;
                foreach (var message in messages.Where(message => message.IsChannelPost).OrderBy(message => message.Date))
                {
                    var messagePublishedAtUtc = DateTimeOffset.FromUnixTimeSeconds(message.Date);
                    if (sinceUtc is not null && messagePublishedAtUtc <= sinceUtc.Value)
                    {
                        reachedSyncedHistory = true;
                        continue;
                    }

                    var post = await ToCollectedPostAsync(client, channel, chat.Id, message, cancellationToken);
                    posts.Add(post);
                }

                if (reachedSyncedHistory || messages.Length < ChatHistoryPageSize)
                {
                    break;
                }

                fromMessageId = messages.Min(message => message.Id);
            }

            return posts;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to fetch recent posts for channel {ChannelReference}", channel.UsernameOrInviteLink);
            await errorAlertService.SendAsync(
                "Collector failed to fetch posts",
                $"Channel: {channel.ChannelName}\nReference: {channel.UsernameOrInviteLink}",
                exception,
                cancellationToken);
            return Array.Empty<CollectedPostDto>();
        }
    }

    private async Task<TdApi.Chat> ResolveChatAsync(TdClient client, TrackedChannel channel, CancellationToken cancellationToken)
    {
        if (long.TryParse(channel.TelegramChannelId, out var knownChatId))
        {
            return await client.ExecuteAsync(new TdApi.GetChat { ChatId = knownChatId });
        }

        var reference = channel.UsernameOrInviteLink.Trim();
        if (TelegramChannelLinkHelper.IsInviteLink(reference))
        {
            var inviteLink = TelegramChannelLinkHelper.NormalizeInviteLink(reference);
            var inviteInfo = await client.ExecuteAsync(new TdApi.CheckChatInviteLink { InviteLink = inviteLink });
            if (inviteInfo.ChatId != 0)
            {
                return await client.ExecuteAsync(new TdApi.GetChat { ChatId = inviteInfo.ChatId });
            }

            return await client.ExecuteAsync(new TdApi.JoinChatByInviteLink { InviteLink = inviteLink });
        }

        var chat = await client.ExecuteAsync(new TdApi.SearchPublicChat
        {
            Username = TelegramChannelLinkHelper.ResolvePublicUsername(reference, channel.NormalizedChannelKey)
        });
        return chat;
    }

    private static async Task<CollectedPostDto> ToCollectedPostAsync(
        TdClient client,
        TrackedChannel channel,
        long chatId,
        TdApi.Message message,
        CancellationToken cancellationToken)
    {
        var text = ExtractText(message.Content);
        var publishedAtUtc = DateTimeOffset.FromUnixTimeSeconds(message.Date);
        var originalPostUrl = await BuildOriginalPostUrlAsync(client, channel, chatId, message.Id);
        var metadataJson = await BuildMetadataJsonAsync(client, chatId, message, cancellationToken);

        return new CollectedPostDto(
            message.Id,
            publishedAtUtc,
            text,
            message.MediaAlbumId == 0 ? null : message.MediaAlbumId.ToString(),
            HasMedia(message.Content),
            message.ForwardInfo is not null,
            message.AuthorSignature,
            originalPostUrl,
            metadataJson);
    }

    private static string ExtractText(TdApi.MessageContent content) =>
        content switch
        {
            TdApi.MessageContent.MessageText text => text.Text.Text,
            TdApi.MessageContent.MessagePhoto photo => ExtractFormattedText(photo.Caption),
            TdApi.MessageContent.MessageVideo video => ExtractFormattedText(video.Caption),
            TdApi.MessageContent.MessageAnimation animation => ExtractFormattedText(animation.Caption),
            TdApi.MessageContent.MessageDocument document => ExtractFormattedText(document.Caption),
            TdApi.MessageContent.MessageAudio audio => ExtractFormattedText(audio.Caption),
            TdApi.MessageContent.MessageVoiceNote voiceNote => ExtractFormattedText(voiceNote.Caption),
            TdApi.MessageContent.MessagePoll poll => ExtractFormattedText(poll.Poll.Question),
            _ => HumanizeContentType(content.DataType)
        };

    private static bool HasMedia(TdApi.MessageContent content) =>
        content is not TdApi.MessageContent.MessageText;

    private static string ExtractFormattedText(TdApi.FormattedText? text)
    {
        var normalized = text?.Text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "(media post)" : normalized;
    }

    private static string HumanizeContentType(string dataType) =>
        dataType switch
        {
            "messagePhoto" => "(photo post)",
            "messageVideo" => "(video post)",
            "messageAnimation" => "(animation post)",
            "messageDocument" => "(document post)",
            "messageAudio" => "(audio post)",
            "messageVoiceNote" => "(voice message)",
            _ => $"({dataType})"
        };

    private static async Task<string?> BuildOriginalPostUrlAsync(TdClient client, TrackedChannel channel, long chatId, long messageId)
    {
        try
        {
            var link = await client.ExecuteAsync(new TdApi.GetMessageLink
            {
                ChatId = chatId,
                MessageId = messageId,
                MediaTimestamp = 0,
                ForAlbum = false,
                InMessageThread = false
            });

            if (!string.IsNullOrWhiteSpace(link.Link))
            {
                return link.Link;
            }
        }
        catch
        {
            // Fall back to best-effort URL construction for public channels.
        }

        var reference = channel.UsernameOrInviteLink.Trim();
        if (TelegramChannelLinkHelper.IsInviteLink(reference) &&
            TelegramChannelLinkHelper.TryBuildPrivatePostUrl(chatId, messageId, out var inviteOnlyPostUrl))
        {
            return inviteOnlyPostUrl;
        }

        var publicPostUrl = TelegramChannelLinkHelper.BuildPublicPostUrl(reference, messageId);
        if (!string.IsNullOrWhiteSpace(publicPostUrl))
        {
            return publicPostUrl;
        }

        if (TelegramChannelLinkHelper.TryBuildPrivatePostUrl(chatId, messageId, out var privatePostUrl))
        {
            return privatePostUrl;
        }

        return $"https://t.me/{channel.NormalizedChannelKey}/{messageId}";
    }

    private static async Task<string> BuildMetadataJsonAsync(
        TdClient client,
        long chatId,
        TdApi.Message message,
        CancellationToken cancellationToken)
    {
        var metadata = new PostMediaMetadata
        {
            ChatId = chatId,
            MessageId = message.Id,
            ContentType = message.Content.DataType
        };

        switch (message.Content)
        {
            case TdApi.MessageContent.MessagePhoto photo:
                metadata.MediaKind = "photo";
                metadata.MediaLocalPath = await DownloadFileAndGetPathAsync(
                    client,
                    photo.Photo.Sizes.OrderByDescending(size => size.Width * size.Height).FirstOrDefault()?.Photo,
                    cancellationToken);
                break;
            case TdApi.MessageContent.MessageVideo video:
                metadata.MediaKind = "video";
                metadata.MediaLocalPath = await DownloadFileAndGetPathAsync(client, video.Video.Video_, cancellationToken);
                break;
        }

        return PostMediaMetadata.Serialize(metadata);
    }

    private static async Task<string?> DownloadFileAndGetPathAsync(TdClient client, TdApi.File? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return null;
        }

        var local = file.Local;
        if (local.IsDownloadingCompleted && !string.IsNullOrWhiteSpace(local.Path))
        {
            EnsureWorldReadable(local.Path);
            return local.Path;
        }

        var downloaded = await client.ExecuteAsync(new TdApi.DownloadFile
        {
            FileId = file.Id,
            Priority = 16,
            Offset = 0,
            Limit = 0,
            Synchronous = true
        });

        if (downloaded.Local.IsDownloadingCompleted && !string.IsNullOrWhiteSpace(downloaded.Local.Path))
        {
            EnsureWorldReadable(downloaded.Local.Path);
            return downloaded.Local.Path;
        }

        return null;
    }

    private static void EnsureWorldReadable(string path)
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead |
                    UnixFileMode.OtherRead);
            }
        }
        catch
        {
            // Best-effort permission fix for local Bot API file access.
        }
    }
}
