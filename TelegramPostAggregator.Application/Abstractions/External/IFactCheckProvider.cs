using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Domain.Entities;

namespace TelegramPostAggregator.Application.Abstractions.External;

public interface IFactCheckProvider
{
    Task<FactCheckResultDto> FactCheckAsync(FactCheckRequest request, TelegramPost post, CancellationToken cancellationToken = default);
}
