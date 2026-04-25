using System.Text.RegularExpressions;
using System.Text.Json;
using TdLib;
using TelegramPostAggregator.Application.Abstractions.External;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;
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
    private static readonly Regex TelegramLinkRegex = new(@"^(?:https?://)?t\.me/(?<slug>[^/?#]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const int HistoryPageSize = 100;
    private const int MaxHistoryPages = 20;

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
                "Failed to join channel",
                $"Channel: {channel.UsernameOrInviteLink}",
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
            var seenMessageIds = new HashSet<long>();
            long fromMessageId = 0;

            for (var page = 0; page < MaxHistoryPages; page++)
            {
                var history = await client.ExecuteAsync(new TdApi.GetChatHistory
                {
                    ChatId = chat.Id,
                    FromMessageId = fromMessageId,
                    Offset = fromMessageId == 0 ? 0 : -1,
                    Limit = HistoryPageSize,
                    OnlyLocal = false
                });

                if (history.Messages_.Count() == 0)
                {
                    break;
                }

                var reachedSinceBoundary = false;
                foreach (var message in history.Messages_.Where(message => message.IsChannelPost).OrderBy(message => message.Date))
                {
                    if (!seenMessageIds.Add(message.Id))
                    {
                        continue;
                    }

                    var publishedAtUtc = DateTimeOffset.FromUnixTimeSeconds(message.Date);
                    if (sinceUtc is not null && publishedAtUtc <= sinceUtc.Value)
                    {
                        reachedSinceBoundary = true;
                        continue;
                    }

                    if (TelegramContentClassifier.IsIgnorableContent(message.Content))
                    {
                        continue;
                    }

                    var post = await ToCollectedPostAsync(client, channel, chat.Id, message, cancellationToken);
                    posts.Add(post);
                }

                var oldestMessageId = history.Messages_.Min(message => message.Id);
                if (reachedSinceBoundary || history.Messages_.Count() < HistoryPageSize || oldestMessageId <= 0)
                {
                    break;
                }

                fromMessageId = oldestMessageId;
            }

            return posts;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to fetch recent posts for channel {ChannelReference}", channel.UsernameOrInviteLink);
            await errorAlertService.SendAsync(
                "Failed to fetch recent posts",
                $"Channel: {channel.UsernameOrInviteLink}",
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
        if (reference.Contains("/+") || reference.Contains("joinchat", StringComparison.OrdinalIgnoreCase))
        {
            return await client.ExecuteAsync(new TdApi.JoinChatByInviteLink { InviteLink = reference });
        }

        var username = channel.NormalizedChannelKey;
        if (reference.StartsWith('@'))
        {
            username = reference[1..];
        }
        else
        {
            var match = TelegramLinkRegex.Match(reference);
            if (match.Success)
            {
                username = match.Groups["slug"].Value;
            }
        }

        var chat = await client.ExecuteAsync(new TdApi.SearchPublicChat { Username = username });
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
            GetMediaGroupId(message),
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
            TdApi.MessageContent.MessageVideoNote => "(video note)",
            TdApi.MessageContent.MessagePoll poll => ExtractFormattedText(poll.Poll.Question),
            _ => HumanizeContentType(content.DataType)
        };

    private static bool HasMedia(TdApi.MessageContent content) =>
        content is not TdApi.MessageContent.MessageText;

    private static string? GetMediaGroupId(TdApi.Message message) =>
        message.MediaAlbumId == 0 ? null : message.MediaAlbumId.ToString();

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
            "messageVideoNote" => "(video note)",
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
        var match = TelegramLinkRegex.Match(reference);
        if (match.Success)
        {
            return $"https://t.me/{match.Groups["slug"].Value}/{messageId}";
        }

        if (reference.StartsWith('@'))
        {
            return $"https://t.me/{reference[1..]}/{messageId}";
        }

        return $"https://t.me/{channel.NormalizedChannelKey}/{messageId}";
    }

    private static Task<string> BuildMetadataJsonAsync(
        TdClient client,
        long chatId,
        TdApi.Message message,
        CancellationToken cancellationToken)
    {
        var metadata = new PostMetadata
        {
            ChatId = chatId,
            MessageId = message.Id,
            ContentType = message.Content.DataType
        };

        switch (message.Content)
        {
            case TdApi.MessageContent.MessagePhoto photo:
                metadata.MediaKind = "photo";
                metadata.MediaFileId = photo.Photo.Sizes
                    .OrderByDescending(size => size.Width * size.Height)
                    .FirstOrDefault()
                    ?.Photo
                    ?.Id;
                break;
            case TdApi.MessageContent.MessageVideo video:
                metadata.MediaKind = "video";
                metadata.MediaFileId = video.Video.Video_.Id;
                break;
            case TdApi.MessageContent.MessageAudio audio:
                metadata.MediaKind = "audio";
                metadata.MediaFileId = audio.Audio.Audio_.Id;
                break;
            case TdApi.MessageContent.MessageVoiceNote voiceNote:
                metadata.MediaKind = "voice";
                metadata.MediaFileId = voiceNote.VoiceNote.Voice.Id;
                break;
            case TdApi.MessageContent.MessageDocument document:
                metadata.MediaKind = "document";
                metadata.MediaFileId = document.Document.Document_.Id;
                break;
            case TdApi.MessageContent.MessageAnimation animation:
                metadata.MediaKind = "animation";
                metadata.MediaFileId = animation.Animation.Animation_.Id;
                break;
            case TdApi.MessageContent.MessageVideoNote videoNote:
                metadata.MediaKind = "video_note";
                metadata.MediaFileId = videoNote.VideoNote.Video.Id;
                break;
        }

        return Task.FromResult(JsonSerializer.Serialize(metadata));
    }

    private sealed class PostMetadata
    {
        public long ChatId { get; set; }
        public long MessageId { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string? MediaKind { get; set; }
        public int? MediaFileId { get; set; }
        public string? MediaLocalPath { get; set; }
    }
}
