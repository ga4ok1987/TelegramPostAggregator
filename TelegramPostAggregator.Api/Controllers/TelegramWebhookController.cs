using Microsoft.AspNetCore.Mvc;
using TelegramPostAggregator.Api.Models;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Api.Controllers;

[ApiController]
[Route("api/telegram/webhook")]
public sealed class TelegramWebhookController(IBotUpdateProcessor botUpdateProcessor) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(BotCommandResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BotCommandResultDto>> Process([FromBody] TelegramWebhookUpdateRequest request, CancellationToken cancellationToken)
    {
        if (request.Message?.From is null)
        {
            return Ok(new BotCommandResultDto(true, "No actionable message in update."));
        }

        var update = new TelegramBotUpdateDto(
            request.UpdateId,
            new BotUserSnapshotDto(
                request.Message.From.Id,
                request.Message.From.Username ?? string.Empty,
                string.Join(' ', new[] { request.Message.From.FirstName, request.Message.From.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                request.Message.From.LanguageCode),
            request.Message.Text,
            request.Message.Chat?.Id,
            DateTimeOffset.UtcNow);

        var result = await botUpdateProcessor.ProcessAsync(update, cancellationToken);
        return Ok(result);
    }
}
