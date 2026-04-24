using Microsoft.AspNetCore.Mvc;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Api.Controllers;

[ApiController]
[Route("api/fact-checks")]
public sealed class FactChecksController(IFactCheckService factCheckService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(FactCheckRequestDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FactCheckRequestDto>> Queue([FromBody] CreateFactCheckRequestDto request, CancellationToken cancellationToken)
    {
        var queued = await factCheckService.QueueRequestAsync(request, cancellationToken);
        return Ok(queued);
    }
}
