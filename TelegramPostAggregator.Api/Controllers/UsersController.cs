using Microsoft.AspNetCore.Mvc;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserService userService) : ControllerBase
{
    [HttpPost("telegram")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> UpsertTelegramUser([FromBody] BotUserSnapshotDto request, CancellationToken cancellationToken)
    {
        var user = await userService.UpsertTelegramUserAsync(request, cancellationToken);
        return Ok(user);
    }
}
