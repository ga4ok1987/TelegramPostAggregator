using TdLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TelegramPostAggregator.Application.Abstractions.Repositories;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Domain.Entities;
using TelegramPostAggregator.Domain.Enums;
using TelegramPostAggregator.Infrastructure.Models;

namespace TelegramPostAggregator.Infrastructure.Services;

public sealed class TdLibRealtimePostIngestionService(
    IServiceScopeFactory scopeFactory,
    IImmediateDeliverySignal deliverySignal,
    IErrorAlertService errorAlertService,
    ILogger<TdLibRealtimePostIngestionService> logger)
{
    private static readonly TimeSpan AlbumDebounceDelay = TimeSpan.FromSeconds(4);
    private readonly Lock _albumGate = new();
    private readonly Dictionary<string, CancellationTokenSource> _albumDebounces = [];

    public async Task HandleNewMessageAsync(
        TdClient client,
        CollectorAccount collectorAccount,
        TdApi.Message message,
        CancellationToken cancellationToken = default)
    {
        if (!message.IsChannelPost)
        {
            return;
        }

        if (TelegramContentClassifier.IsIgnorableContent(message.Content))
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var channelRepository = scope.ServiceProvider.GetRequiredService<ITrackedChannelRepository>();
            var postRepository = scope.ServiceProvider.GetRequiredService<IPostRepository>();
            var textNormalizer = scope.ServiceProvider.GetRequiredService<ITextNormalizer>();

            var channel = await channelRepository.GetByTelegramChannelIdAsync(message.ChatId.ToString(), cancellationToken);
            if (channel is null || channel.Status != ChannelTrackingStatus.Active)
            {
                return;
            }

            var existing = await postRepository.GetByChannelAndMessageIdAsync(channel.Id, message.Id, cancellationToken);
            if (existing is not null)
            {
                return;
            }

            var text = ExtractText(message.Content);
            var normalizedText = textNormalizer.Normalize(text);
            var metadataJson = await BuildMetadataJsonAsync(client, message.ChatId, message, cancellationToken);
            var post = new TelegramPost
            {
                ChannelId = channel.Id,
                CollectorAccountId = collectorAccount.Id,
                TelegramMessageId = message.Id,
                PublishedAtUtc = DateTimeOffset.FromUnixTimeSeconds(message.Date),
                AuthorSignature = message.AuthorSignature,
                RawText = text,
                NormalizedText = normalizedText,
                MediaGroupId = message.MediaAlbumId == 0 ? null : message.MediaAlbumId.ToString(),
                HasMedia = HasMedia(message.Content),
                IsForwarded = message.ForwardInfo is not null,
                OriginalPostUrl = await BuildOriginalPostUrlAsync(client, channel, message.ChatId, message.Id),
                MetadataJson = metadataJson
            };

            await postRepository.AddAsync(post, cancellationToken);
            await postRepository.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Realtime post ingested for channel {ChannelName}: messageId={TelegramMessageId}, albumId={MediaGroupId}",
                channel.ChannelName,
                message.Id,
                post.MediaGroupId);

            SignalDelivery(post.MediaGroupId);
        }
        catch (DbUpdateException exception) when (IsDuplicatePostInsert(exception))
        {
            logger.LogInformation(
                "Realtime post {MessageId} for chat {ChatId} was already stored by another ingestion path. Skipping duplicate insert.",
                message.Id,
                message.ChatId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Realtime post ingestion failed for chat {ChatId}, message {MessageId}", message.ChatId, message.Id);
            await errorAlertService.SendAsync(
                "Realtime post ingestion failed",
                $"ChatId: {message.ChatId}\nMessageId: {message.Id}",
                exception,
                cancellationToken);
        }
    }

    private void SignalDelivery(string? mediaGroupId)
    {
        if (string.IsNullOrWhiteSpace(mediaGroupId))
        {
            deliverySignal.Signal();
            return;
        }

        CancellationTokenSource cts;
        lock (_albumGate)
        {
            if (_albumDebounces.Remove(mediaGroupId, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            cts = new CancellationTokenSource();
            _albumDebounces[mediaGroupId] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AlbumDebounceDelay, cts.Token);
                deliverySignal.Signal();
            }
            catch (OperationCanceledException)
            {
                // Another album item arrived; the next debounce will signal delivery.
            }
            finally
            {
                lock (_albumGate)
                {
                    if (_albumDebounces.TryGetValue(mediaGroupId, out var current) && ReferenceEquals(current, cts))
                    {
                        _albumDebounces.Remove(mediaGroupId);
                    }
                }

                cts.Dispose();
            }
        });
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
            _ => $"({content.DataType})"
        };

    private static bool HasMedia(TdApi.MessageContent content) =>
        content is not TdApi.MessageContent.MessageText;

    private static string ExtractFormattedText(TdApi.FormattedText? text)
    {
        var normalized = text?.Text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "(media post)" : normalized;
    }

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
            // Fall back to best-effort URL construction below.
        }

        var reference = channel.UsernameOrInviteLink.Trim();
        if (TelegramChannelLinkHelper.IsInviteLink(reference) &&
            TelegramChannelLinkHelper.TryBuildPrivatePostUrl(chatId, messageId, out var inviteOnlyPostUrl))
        {
            return inviteOnlyPostUrl;
        }

        return TelegramChannelLinkHelper.BuildPublicPostUrl(reference, messageId)
            ?? (TelegramChannelLinkHelper.TryBuildPrivatePostUrl(chatId, messageId, out var privatePostUrl)
                ? privatePostUrl
                : $"https://t.me/{channel.NormalizedChannelKey}/{messageId}");
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
                metadata.MediaFileId = photo.Photo.Sizes.OrderByDescending(size => size.Width * size.Height).FirstOrDefault()?.Photo?.Id;
                metadata.MediaLocalPath = await DownloadFileAndGetPathAsync(
                    client,
                    photo.Photo.Sizes.OrderByDescending(size => size.Width * size.Height).FirstOrDefault()?.Photo,
                    cancellationToken);
                break;
            case TdApi.MessageContent.MessageVideo video:
                metadata.MediaKind = "video";
                metadata.MediaFileId = video.Video.Video_.Id;
                metadata.MediaLocalPath = await DownloadFileAndGetPathAsync(client, video.Video.Video_, cancellationToken);
                break;
            case TdApi.MessageContent.MessageAudio audio:
                metadata.MediaKind = "audio";
                metadata.MediaFileId = audio.Audio.Audio_.Id;
                metadata.MediaLocalPath = await DownloadFileAndGetPathAsync(client, audio.Audio.Audio_, cancellationToken);
                break;
            case TdApi.MessageContent.MessageVoiceNote voiceNote:
                metadata.MediaKind = "voice";
                metadata.MediaFileId = voiceNote.VoiceNote.Voice.Id;
                metadata.MediaLocalPath = await DownloadFileAndGetPathAsync(client, voiceNote.VoiceNote.Voice, cancellationToken);
                break;
            case TdApi.MessageContent.MessageDocument document:
                metadata.MediaKind = "document";
                metadata.MediaFileId = document.Document.Document_.Id;
                metadata.MediaLocalPath = await DownloadFileAndGetPathAsync(client, document.Document.Document_, cancellationToken);
                break;
            case TdApi.MessageContent.MessageAnimation animation:
                metadata.MediaKind = "animation";
                metadata.MediaFileId = animation.Animation.Animation_.Id;
                metadata.MediaLocalPath = await DownloadFileAndGetPathAsync(client, animation.Animation.Animation_, cancellationToken);
                break;
            case TdApi.MessageContent.MessageVideoNote videoNote:
                metadata.MediaKind = "video_note";
                metadata.MediaFileId = videoNote.VideoNote.Video.Id;
                metadata.MediaLocalPath = await DownloadFileAndGetPathAsync(client, videoNote.VideoNote.Video, cancellationToken);
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

        if (file.Local.IsDownloadingCompleted && !string.IsNullOrWhiteSpace(file.Local.Path))
        {
            EnsureWorldReadable(file.Local.Path);
            return file.Local.Path;
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

    private static bool IsDuplicatePostInsert(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        string.Equals(postgresException.ConstraintName, "IX_telegram_posts_ChannelId_TelegramMessageId", StringComparison.Ordinal);
}
