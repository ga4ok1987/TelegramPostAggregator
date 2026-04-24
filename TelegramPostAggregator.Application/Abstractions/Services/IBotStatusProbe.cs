using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IBotStatusProbe
{
    string ProbeType { get; }

    Task<BotProbeResultDto> CheckAsync(BotDefinitionOptions bot, CancellationToken cancellationToken = default);
}
