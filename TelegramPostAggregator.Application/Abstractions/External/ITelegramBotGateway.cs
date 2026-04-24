using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.External;

public interface ITelegramBotGateway
{
    Task<IReadOnlyList<TelegramBotUpdateDto>> GetUpdatesAsync(long offset, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendMessageAsync(TelegramBotOutboundMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendPhotoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> SendVideoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto?> SendMediaGroupAsync(TelegramBotMediaGroupMessageDto message, CancellationToken cancellationToken = default);

    Task<TelegramBotApiResultDto> AnswerCallbackQueryAsync(string callbackQueryId, string? text, CancellationToken cancellationToken = default);
}
