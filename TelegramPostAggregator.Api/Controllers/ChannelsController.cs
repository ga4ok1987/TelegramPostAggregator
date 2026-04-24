using Microsoft.AspNetCore.Mvc;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Api.Controllers;

[ApiController]
[Route("api/channels")]
public sealed class ChannelsController(IChannelTrackingService channelTrackingService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChannelDto>>> GetForUser([FromQuery] long telegramUserId, CancellationToken cancellationToken)
    {
        var channels = await channelTrackingService.ListTrackedChannelsAsync(telegramUserId, cancellationToken);
        return Ok(channels);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ChannelDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChannelDto>> Add([FromBody] AddTrackedChannelDto request, CancellationToken cancellationToken)
    {
        var channel = await channelTrackingService.AddTrackedChannelAsync(request, cancellationToken);
        return Ok(channel);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove([FromBody] RemoveTrackedChannelDto request, CancellationToken cancellationToken)
    {
        await channelTrackingService.RemoveTrackedChannelAsync(request, cancellationToken);
        return NoContent();
    }
}
