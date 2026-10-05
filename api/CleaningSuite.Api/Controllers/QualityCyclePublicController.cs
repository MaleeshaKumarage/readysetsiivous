using CleaningSuite.Application.QualityCycle;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/public/{slug}/quality-cycle")]
[AllowAnonymous]
public class QualityCyclePublicController : ControllerBase
{
    private readonly IMediator _mediator;

    public QualityCyclePublicController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> GetByToken(string token, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetQualityCycleFormByTokenQuery(token), ct);
        return Ok(result);
    }

    [HttpPost("{token}/submit")]
    public async Task<IActionResult> Submit(string token, [FromBody] SubmitQualityCycleFormCommand command, CancellationToken ct)
    {
        command = command with { Token = token };
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}
