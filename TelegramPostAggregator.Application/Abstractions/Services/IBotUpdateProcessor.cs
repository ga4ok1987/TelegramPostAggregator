using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IBotUpdateProcessor
{
    Task<BotCommandResultDto> ProcessAsync(TelegramBotUpdateDto update, CancellationToken cancellationToken = default);
}
