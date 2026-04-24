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
        var sourceUser = request.Message?.From ?? request.CallbackQuery?.From;
        var chatId = request.Message?.Chat?.Id ?? request.CallbackQuery?.Message?.Chat?.Id;
        if (sourceUser is null)
        {
            return Ok(new BotCommandResultDto(true, "No actionable message in update."));
        }

        var update = new TelegramBotUpdateDto(
            request.UpdateId,
            new BotUserSnapshotDto(
                sourceUser.Id,
                sourceUser.Username ?? string.Empty,
                string.Join(' ', new[] { sourceUser.FirstName, sourceUser.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                sourceUser.LanguageCode),
            request.Message?.Text,
            request.CallbackQuery?.Id,
            request.CallbackQuery?.Data,
            chatId,
            DateTimeOffset.UtcNow);

        var result = await botUpdateProcessor.ProcessAsync(update, cancellationToken);
        return Ok(result);
    }
}
