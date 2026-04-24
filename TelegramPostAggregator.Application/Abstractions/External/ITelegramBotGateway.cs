using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.External;

public interface ITelegramBotGateway
{
    Task<IReadOnlyList<TelegramBotUpdateDto>> GetUpdatesAsync(long offset, CancellationToken cancellationToken = default);
    Task SendMessageAsync(TelegramBotOutboundMessageDto message, CancellationToken cancellationToken = default);
    Task SendPhotoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);
    Task SendVideoAsync(TelegramBotMediaMessageDto message, CancellationToken cancellationToken = default);
    Task SendMediaGroupAsync(TelegramBotMediaGroupMessageDto message, CancellationToken cancellationToken = default);
}
