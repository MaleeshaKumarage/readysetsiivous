using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Agreements.Queries;
using MediatR;
using FluentValidation;
using FluentValidation.Results;
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
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> Sign(string token, [FromBody] SignRequest body, CancellationToken ct)
    {
        var png = DecodeSignature(body.SignaturePng);
        var result = await _mediator.Send(new SignAgreementCommand(token, body.TypedName ?? string.Empty, png), ct);
        return Ok(result);
    }

    [HttpGet("{token}/file")]
    public async Task<IActionResult> File(string token, CancellationToken ct)
    {
        var path = await _mediator.Send(new GetAgreementFileQuery(token), ct);
        if (path is null) return NotFound();
        return PhysicalFile(path, "application/pdf");
    }

    private static byte[] DecodeSignature(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException([new ValidationFailure("SignaturePng", "Signature image is required.")]);
        var comma = value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? value.IndexOf(',') : -1;
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma < 0)
            throw new ValidationException([new ValidationFailure("SignaturePng", "Signature image data is malformed.")]);
        try { return Convert.FromBase64String(comma >= 0 ? value[(comma + 1)..] : value); }
        catch (FormatException)
        {
            throw new ValidationException([new ValidationFailure("SignaturePng", "Signature image base64 data is malformed.")]);
        }
    }
}

public record SignRequest(string? TypedName, string? SignaturePng);
