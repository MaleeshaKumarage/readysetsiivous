using CleaningSuite.Api.Auth;
using CleaningSuite.Application.QualityCycle;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/quality-cycle")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class QualityCycleAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public QualityCycleAdminController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("templates")]
    public async Task<IActionResult> ListTemplates(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? branchId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListQualityCycleTemplatesQuery(companyId, branchId), ct);
        return Ok(result);
    }

    [HttpGet("templates/{id:guid}")]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetQualityCycleTemplateQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateQualityCycleTemplateCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPut("templates/{id:guid}")]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] UpdateQualityCycleTemplateCommand command, CancellationToken ct)
    {
        command = command with { Id = id };
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpDelete("templates/{id:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteQualityCycleTemplateCommand(id), ct);
        return NoContent();
    }

    [HttpGet("forms")]
    public async Task<IActionResult> ListForms(
        [FromQuery] Guid? shiftId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListQualityCycleFormsQuery(shiftId, from, to), ct);
        return Ok(result);
    }

    [HttpGet("summary-pdf")]
    public async Task<IActionResult> DownloadSummaryPdf(
        [FromQuery] Guid shiftId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        var pdfBytes = await _mediator.Send(new GetQualityCycleSummaryPdfQuery(shiftId, year, month), ct);
        var fileName = $"QualityCycle_Summary_{shiftId}_{year}_{month:D2}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }
}
