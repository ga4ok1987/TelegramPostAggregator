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
    private static readonly TimeSpan EditDebounceDelay = TimeSpan.FromSeconds(1);
    private readonly Lock _albumGate = new();
    private readonly Dictionary<string, CancellationTokenSource> _albumDebounces = [];
    private readonly Lock _editGate = new();
    private readonly Dictionary<string, EditPendingState> _editDebounces = [];

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
            var postRevisionRepository = scope.ServiceProvider.GetRequiredService<IPostRevisionRepository>();
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
            await postRevisionRepository.AddAsync(
                CreateRevision(post, 1, false, null),
                cancellationToken);
            channel.LastPostCollectedAtUtc = DateTimeOffset.UtcNow;
            channel.LastCollectorError = null;
            channel.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await postRevisionRepository.SaveChangesAsync(cancellationToken);
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

    public Task HandleMessageEditedAsync(
        TdClient client,
        CollectorAccount collectorAccount,
        long chatId,
        long messageId,
        DateTimeOffset? telegramEditDateUtc,
        CancellationToken cancellationToken = default)
    {
        QueueEditedMessageProcessing(client, collectorAccount, chatId, messageId, telegramEditDateUtc, cancellationToken);
        return Task.CompletedTask;
    }

    public Task HandleMessageContentUpdatedAsync(
        TdClient client,
        CollectorAccount collectorAccount,
        long chatId,
        long messageId,
        CancellationToken cancellationToken = default)
    {
        QueueEditedMessageProcessing(client, collectorAccount, chatId, messageId, null, cancellationToken);
        return Task.CompletedTask;
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

    private void QueueEditedMessageProcessing(
        TdClient client,
        CollectorAccount collectorAccount,
        long chatId,
        long messageId,
        DateTimeOffset? telegramEditDateUtc,
        CancellationToken cancellationToken)
    {
        var key = $"{chatId}:{messageId}";
        CancellationTokenSource cts;
        EditPendingState pendingState;

        lock (_editGate)
        {
            if (_editDebounces.TryGetValue(key, out var existing))
            {
                existing.Cancellation.Cancel();
                existing.Cancellation.Dispose();
                pendingState = existing with
                {
                    EditDateUtc = Max(existing.EditDateUtc, telegramEditDateUtc),
                    Cancellation = new CancellationTokenSource()
                };
                _editDebounces[key] = pendingState;
            }
            else
            {
                pendingState = new EditPendingState(
                    client,
                    collectorAccount,
                    chatId,
                    messageId,
                    telegramEditDateUtc,
                    new CancellationTokenSource());
                _editDebounces[key] = pendingState;
            }

            cts = pendingState.Cancellation;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(EditDebounceDelay, cts.Token);
                await ProcessEditedMessageAsync(
                    pendingState.Client,
                    pendingState.CollectorAccount,
                    pendingState.ChatId,
                    pendingState.MessageId,
                    pendingState.EditDateUtc,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Another edit/content update arrived; the newest debounce will process it.
            }
            finally
            {
                lock (_editGate)
                {
                    if (_editDebounces.TryGetValue(key, out var current) && ReferenceEquals(current.Cancellation, cts))
                    {
                        _editDebounces.Remove(key);
                    }
                }

                cts.Dispose();
            }
        });
    }

    private async Task ProcessEditedMessageAsync(
        TdClient client,
        CollectorAccount collectorAccount,
        long chatId,
        long messageId,
        DateTimeOffset? telegramEditDateUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await client.ExecuteAsync(new TdApi.GetMessage
            {
                ChatId = chatId,
                MessageId = messageId
            });

            if (!message.IsChannelPost || TelegramContentClassifier.IsIgnorableContent(message.Content))
            {
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var channelRepository = scope.ServiceProvider.GetRequiredService<ITrackedChannelRepository>();
            var postRepository = scope.ServiceProvider.GetRequiredService<IPostRepository>();
            var postRevisionRepository = scope.ServiceProvider.GetRequiredService<IPostRevisionRepository>();
            var textNormalizer = scope.ServiceProvider.GetRequiredService<ITextNormalizer>();

            var channel = await channelRepository.GetByTelegramChannelIdAsync(chatId.ToString(), cancellationToken);
            if (channel is null || channel.Status != ChannelTrackingStatus.Active)
            {
                return;
            }

            var post = await postRepository.GetByChannelAndMessageIdAsync(channel.Id, messageId, cancellationToken);
            if (post is null)
            {
                return;
            }

            var text = ExtractText(message.Content);
            var normalizedText = textNormalizer.Normalize(text);
            var metadataJson = await BuildMetadataJsonAsync(client, chatId, message, cancellationToken);
            var originalPostUrl = await BuildOriginalPostUrlAsync(client, channel, chatId, messageId);
            var mediaGroupId = message.MediaAlbumId == 0 ? null : message.MediaAlbumId.ToString();
            var hasMedia = HasMedia(message.Content);

            var hasChanges =
                !string.Equals(post.RawText, text, StringComparison.Ordinal) ||
                !string.Equals(post.NormalizedText, normalizedText, StringComparison.Ordinal) ||
                !string.Equals(post.MetadataJson, metadataJson, StringComparison.Ordinal) ||
                !string.Equals(post.MediaGroupId, mediaGroupId, StringComparison.Ordinal) ||
                post.HasMedia != hasMedia ||
                !string.Equals(post.OriginalPostUrl, originalPostUrl, StringComparison.Ordinal) ||
                !string.Equals(post.AuthorSignature, message.AuthorSignature, StringComparison.Ordinal);

            var effectiveEditDateUtc = telegramEditDateUtc ?? DateTimeOffset.UtcNow;
            if (!hasChanges && post.IsEdited && post.TelegramEditDateUtc == effectiveEditDateUtc)
            {
                return;
            }

            if (!await postRevisionRepository.AnyForPostAsync(post.Id, cancellationToken))
            {
                await postRevisionRepository.AddAsync(CreateRevision(post, 1, post.IsEdited, post.TelegramEditDateUtc), cancellationToken);
            }

            post.RawText = text;
            post.NormalizedText = normalizedText;
            post.MetadataJson = metadataJson;
            post.MediaGroupId = mediaGroupId;
            post.HasMedia = hasMedia;
            post.IsForwarded = message.ForwardInfo is not null;
            post.OriginalPostUrl = originalPostUrl;
            post.AuthorSignature = message.AuthorSignature;
            post.IsEdited = true;
            post.TelegramEditDateUtc = effectiveEditDateUtc;
            post.UpdatedAtUtc = DateTimeOffset.UtcNow;

            var nextRevisionNumber = await postRevisionRepository.GetNextRevisionNumberAsync(post.Id, cancellationToken);
            await postRevisionRepository.AddAsync(CreateRevision(post, nextRevisionNumber, true, effectiveEditDateUtc), cancellationToken);

            channel.LastPostCollectedAtUtc = DateTimeOffset.UtcNow;
            channel.LastCollectorError = null;
            channel.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await postRevisionRepository.SaveChangesAsync(cancellationToken);
            await postRepository.SaveChangesAsync(cancellationToken);
            await channelRepository.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Realtime post edit ingested for channel {ChannelName}: messageId={TelegramMessageId}, revision={RevisionNumber}",
                channel.ChannelName,
                messageId,
                nextRevisionNumber);

            deliverySignal.Signal();
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
            case TdApi.MessageContent.MessageVideoNote videoNote:
                metadata.MediaKind = "video_note";
                metadata.MediaFileId = videoNote.VideoNote.Video.Id;
                break;
        }

        return PostMediaMetadata.Serialize(metadata);
    }

    private static TelegramPostRevision CreateRevision(
        TelegramPost post,
        int revisionNumber,
        bool isEdited,
        DateTimeOffset? telegramEditDateUtc) =>
        new()
        {
            PostId = post.Id,
            RevisionNumber = revisionNumber,
            IsEdited = isEdited,
            TelegramEditDateUtc = telegramEditDateUtc,
            RawText = post.RawText,
            NormalizedText = post.NormalizedText,
            MediaGroupId = post.MediaGroupId,
            HasMedia = post.HasMedia,
            OriginalPostUrl = post.OriginalPostUrl,
            MetadataJson = post.MetadataJson
        };

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null ? left : left >= right ? left : right;

    private static bool IsDuplicatePostInsert(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        string.Equals(postgresException.ConstraintName, "IX_telegram_posts_ChannelId_TelegramMessageId", StringComparison.Ordinal);

    private sealed record EditPendingState(
        TdClient Client,
        CollectorAccount CollectorAccount,
        long ChatId,
        long MessageId,
        DateTimeOffset? EditDateUtc,
        CancellationTokenSource Cancellation);
}
