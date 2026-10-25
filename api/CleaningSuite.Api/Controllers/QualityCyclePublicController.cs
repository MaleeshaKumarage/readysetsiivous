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
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Submit(string token, [FromBody] SubmitQualityCycleFormCommand command, CancellationToken ct)
    {
        command = command with { Token = token };
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
    [HttpGet("reports/{token}/pdf")]
    public async Task<IActionResult> SharedReportPdf(string token, CancellationToken ct) {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(await _mediator.Send(new GetSharedQualityCycleReportPdfQuery(token), ct), "application/pdf", "Monthly_Activity_Report.pdf");
    }

    [HttpGet("reports/{token}")]
    public async Task<IActionResult> SharedReport(string token, CancellationToken ct) {
        PrivateReportHeaders();
        return Ok(await _mediator.Send(new GetSharedQualityCycleReportQuery(token), ct));
    }

    [HttpPost("reports/{token}/acknowledge")]
    public async Task<IActionResult> AcknowledgeReport(string token, CancellationToken ct) {
        PrivateReportHeaders();
        return Ok(await _mediator.Send(new AcknowledgeQualityCycleReportCommand(token), ct));
    }

    private void PrivateReportHeaders() {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
