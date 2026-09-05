using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Invoicing.Commands;
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

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] DateTime? fromUtc, CancellationToken ct)
    {
        var invoices = await _mediator.Send(new ListInvoicesQuery(status, fromUtc), ct);
        return Ok(invoices);
    }

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
        return invoice is null ? NotFound() : Ok(invoice);
    }

    [HttpPost("{id:guid}/mark-paid")]
    public async Task<IActionResult> MarkPaid(
        Guid id, [FromBody] MarkPaidBody body, CancellationToken ct)
    {
        await _mediator.Send(new MarkInvoicePaidCommand(id, body.PaidAtUtc), ct);
        return NoContent();
    }

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
