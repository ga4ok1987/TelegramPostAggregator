using Microsoft.AspNetCore.Mvc;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Api.Controllers;

[ApiController]
[Route("api/feed")]
public sealed class FeedController(IFeedService feedService) : ControllerBase
{
    [HttpGet("{telegramUserId:long}")]
    [ProducesResponseType(typeof(IReadOnlyList<FeedItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FeedItemDto>>> Get(long telegramUserId, [FromQuery] int take = 50, [FromQuery] int skip = 0, CancellationToken cancellationToken = default)
    {
        var feed = await feedService.GetPersonalFeedAsync(telegramUserId, take, skip, cancellationToken);
        return Ok(feed);
    }
}
