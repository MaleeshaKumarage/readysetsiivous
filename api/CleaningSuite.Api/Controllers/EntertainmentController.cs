using CleaningSuite.Application.Entertainment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/entertainment")]
[AllowAnonymous]
public class EntertainmentController : ControllerBase
{
    private readonly IMediator _mediator;

    public EntertainmentController(IMediator mediator) => _mediator = mediator;

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup(
        [FromBody] CreateEntertainmentGroupCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("groups/{joinKey}/join")]
    public async Task<IActionResult> JoinGroup(
        string joinKey,
        [FromBody] JoinEntertainmentGroupCommand command,
        CancellationToken ct)
    {
        command = command with { JoinKey = joinKey };
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpGet("groups/{groupId:guid}/lobby")]
    public async Task<IActionResult> GetLobby(Guid groupId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEntertainmentLobbyQuery(groupId), ct);
        return Ok(result);
    }
}
