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
    private static readonly TimeSpan EditTrackingWindow = TimeSpan.FromHours(24);
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
                MetadataJson = metadataJson,
                EmbeddingStatus = EmbeddingStatus.Pending
            };

            await postRepository.AddAsync(post, cancellationToken);
            channel.LastPostCollectedAtUtc = DateTimeOffset.UtcNow;
            channel.LastCollectorError = null;
            channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await postRepository.SaveChangesAsync(cancellationToken);
            await channelRepository.SaveChangesAsync(cancellationToken);

            logger.LogTrace(
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

    public async Task HandleEditedMessageAsync(
        TdClient client,
        CollectorAccount collectorAccount,
        long chatId,
        long messageId,
        int editDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var channelRepository = scope.ServiceProvider.GetRequiredService<ITrackedChannelRepository>();
            var postRepository = scope.ServiceProvider.GetRequiredService<IPostRepository>();
            var postTrackingRepository = scope.ServiceProvider.GetRequiredService<IManagedChannelPostTrackingRepository>();
            var textNormalizer = scope.ServiceProvider.GetRequiredService<ITextNormalizer>();

            var channel = await channelRepository.GetByTelegramChannelIdAsync(chatId.ToString(), cancellationToken);
            if (channel is null || channel.Status != ChannelTrackingStatus.Active)
            {
                return;
            }

            var existingPost = await postRepository.GetByChannelAndMessageIdAsync(channel.Id, messageId, cancellationToken);
            if (existingPost is null)
            {
                return;
            }

            if (DateTimeOffset.UtcNow - existingPost.PublishedAtUtc > EditTrackingWindow)
            {
                return;
            }

            var message = await client.ExecuteAsync(new TdApi.GetMessage
            {
                ChatId = chatId,
                MessageId = messageId
            });

            if (!message.IsChannelPost || TelegramContentClassifier.IsIgnorableContent(message.Content))
            {
                return;
            }

            var text = ExtractText(message.Content);
            existingPost.AuthorSignature = message.AuthorSignature;
            existingPost.RawText = text;
            existingPost.NormalizedText = textNormalizer.Normalize(text);
            existingPost.MediaGroupId = message.MediaAlbumId == 0 ? null : message.MediaAlbumId.ToString();
            existingPost.HasMedia = HasMedia(message.Content);
            existingPost.IsForwarded = message.ForwardInfo is not null;
            existingPost.OriginalPostUrl = await BuildOriginalPostUrlAsync(client, channel, chatId, messageId);
            existingPost.MetadataJson = await BuildMetadataJsonAsync(client, chatId, message, cancellationToken);
            if (existingPost.EmbeddingStatus is EmbeddingStatus.Ready or EmbeddingStatus.Failed)
            {
                existingPost.EmbeddingStatus = EmbeddingStatus.PendingRefresh;
                existingPost.EmbeddingLastError = null;
            }
            existingPost.UpdatedAtUtc = DateTimeOffset.UtcNow;

            var pendingTrackings = await postTrackingRepository.GetByPostIdAsync(existingPost.Id, cancellationToken);
            var editedAtUtc = DateTimeOffset.FromUnixTimeSeconds(editDate);
            var markedAny = false;

            foreach (var tracking in pendingTrackings)
            {
                if (!tracking.ManagedChannel.TrackPostEdits ||
                    !tracking.ManagedChannel.TrackPostEditsEnabledAtUtc.HasValue ||
                    tracking.LastDeliveredAtUtc < tracking.ManagedChannel.TrackPostEditsEnabledAtUtc.Value ||
                    tracking.TrackEditsUntilUtc <= DateTimeOffset.UtcNow ||
                    !tracking.ManagedChannel.IsActive ||
                    !tracking.ManagedChannelSubscription.IsActive)
                {
                    continue;
                }

                if (tracking.PendingEditedAtUtc.HasValue && tracking.PendingEditedAtUtc.Value >= editedAtUtc)
                {
                    continue;
                }

                if (tracking.LastProcessedEditedAtUtc.HasValue && tracking.LastProcessedEditedAtUtc.Value >= editedAtUtc)
                {
                    continue;
                }

                tracking.PendingEditedAtUtc = editedAtUtc;
                tracking.UpdatedAtUtc = DateTimeOffset.UtcNow;
                markedAny = true;
            }

            await postRepository.SaveChangesAsync(cancellationToken);
            if (markedAny)
            {
                await postTrackingRepository.SaveChangesAsync(cancellationToken);
                deliverySignal.Signal();
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Realtime post edit ingestion failed for chat {ChatId}, message {MessageId}", chatId, messageId);
            await errorAlertService.SendAsync(
                "Realtime post edit ingestion failed",
                $"ChatId: {chatId}\nMessageId: {messageId}",
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
            TdApi.MessageContent.MessageSticker => "(sticker)",
            TdApi.MessageContent.MessageVideoNote => "(video note)",
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
            case TdApi.MessageContent.MessageSticker sticker:
                metadata.MediaKind = "sticker";
                metadata.MediaFileId = sticker.Sticker.Sticker_.Id;
                break;
            case TdApi.MessageContent.MessageVideoNote videoNote:
                metadata.MediaKind = "video_note";
                metadata.MediaFileId = videoNote.VideoNote.Video.Id;
                break;
        }

        return PostMediaMetadata.Serialize(metadata);
    }

    private static bool IsDuplicatePostInsert(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        string.Equals(postgresException.ConstraintName, "IX_telegram_posts_ChannelId_TelegramMessageId", StringComparison.Ordinal);
}
