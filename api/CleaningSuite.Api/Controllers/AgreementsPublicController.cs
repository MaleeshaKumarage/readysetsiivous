using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Agreements.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/public/{slug}/agreements")]
[AllowAnonymous]
public class AgreementsPublicController : ControllerBase
{
    private readonly IMediator _mediator;
    public AgreementsPublicController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetPublicAgreementQuery(token), ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("{token}/sign")]
    public async Task<IActionResult> Sign(string token, [FromBody] SignRequest body, CancellationToken ct)
    {
        var png = Convert.FromBase64String(Normalize(body.SignaturePng));
        var result = await _mediator.Send(new SignAgreementCommand(token, body.TypedName, png), ct);
        return Ok(result);
    }

    [HttpGet("{token}/file")]
    public async Task<IActionResult> File(string token, CancellationToken ct)
    {
        var path = await _mediator.Send(new GetAgreementFileQuery(token), ct);
        if (path is null) return NotFound();
        return PhysicalFile(path, "application/pdf");
    }

    private static string Normalize(string s) => s.StartsWith("data:") ? s[(s.IndexOf(',') + 1)..] : s;
}

public record SignRequest(string TypedName, string SignaturePng);
