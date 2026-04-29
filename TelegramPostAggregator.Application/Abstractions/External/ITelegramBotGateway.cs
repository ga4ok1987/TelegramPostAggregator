using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.External;

public interface ITelegramBotGateway
{
    Task<IReadOnlyList<TelegramBotUpdateDto>> GetUpdatesAsync(long offset, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendMessageAsync(TelegramBotOutboundMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendPhotoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendVideoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendAudioAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendVoiceAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendDocumentAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendAnimationAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendVideoNoteAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto?> SendMediaGroupAsync(TelegramBotMediaGroupMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken cancellationToken = default);

    Task<bool> IsBotAdministratorAsync(string telegramChannelId, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SetChatMenuButtonAsync(string text, string webAppUrl, CancellationToken cancellationToken = default);

    Task<string?> GetChatProfileImageDataUrlAsync(string telegramChatReference, CancellationToken cancellationToken = default);
}
