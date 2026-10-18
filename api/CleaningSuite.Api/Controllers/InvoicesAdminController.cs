using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Invoicing.Commands;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Invoicing.Queries;
using CleaningSuite.Infrastructure.Invoicing;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Invoicing for the tenant admin.</summary>
[ApiController]
[Route("api/v1/admin/invoices")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class InvoicesAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvoicesAdminController(IMediator mediator) => _mediator = mediator;

    [HttpGet("issuer")]
    public async Task<IActionResult> Issuer([FromServices] ShiftBillingSettingsService billing, CancellationToken ct) =>
        Ok(await billing.GetIssuerAsync(ct));

    [HttpPut("issuer")]
    public async Task<IActionResult> SaveIssuer([FromBody] InvoiceIssuerSettings settings,
        [FromServices] ShiftBillingSettingsService billing, CancellationToken ct) => Ok(await billing.SaveIssuerAsync(settings, ct));

    [HttpPost("shift-preview")]
    public async Task<IActionResult> PreviewShifts([FromBody] ShiftInvoiceRequest request,
        [FromServices] ShiftInvoiceService service, CancellationToken ct)
    {
        var preview = await service.PreviewAsync(request, ct);
        return Ok(AdminResponses.Invoice(preview.Invoice) with { PreviewToken = preview.PreviewToken });
    }

    [HttpPost("shifts")]
    public async Task<IActionResult> IssueShifts([FromBody] ShiftInvoiceRequest request,
        [FromServices] ShiftInvoiceService service, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, AdminResponses.Invoice(await service.IssueAsync(request, ct)));

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] DateTime? fromUtc, CancellationToken ct)
    {
        var invoices = await _mediator.Send(new ListInvoicesQuery(status, fromUtc), ct);
        return Ok(invoices.Select(AdminResponses.Invoice).ToArray());
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceBody body, CancellationToken ct)
    {
        var id = await _mediator.Send(new CreateInvoiceCommand(body.BookingId), ct);
        return StatusCode(StatusCodes.Status201Created, new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var invoice = await _mediator.Send(new GetInvoiceQuery(id), ct);
        return invoice is null ? NotFound() : Ok(AdminResponses.Invoice(invoice));
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost("{id:guid}/mark-paid")]
    public async Task<IActionResult> MarkPaid(
        Guid id, [FromBody] MarkPaidBody body, CancellationToken ct)
    {
        await _mediator.Send(new MarkInvoicePaidCommand(id, body.PaidAtUtc), ct);
        return NoContent();
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost("{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new VoidInvoiceCommand(id), ct);
        return NoContent();
    }

    /// <summary>PDF rendered from stored snapshots, deterministic and regenerable.</summary>
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var invoice = await _mediator.Send(new GetInvoiceQuery(id), ct);
        if (invoice is null)
            return NotFound();

        var bytes = InvoicePdfRenderer.Render(invoice);
        return File(bytes, "application/pdf", $"invoice-{invoice.InvoiceNumber}.pdf");
    }
}

public record CreateInvoiceBody(Guid BookingId);
public record MarkPaidBody(DateTime PaidAtUtc);
