using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Agreements.Queries;
using CleaningSuite.Api.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/agreements")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class AgreementsAdminController : ControllerBase
{
    private readonly IMediator _mediator;
    public AgreementsAdminController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Create([FromForm] string title, [FromForm] IFormFile pdf,
        [FromForm] string signersJson, CancellationToken ct)
    {
        var signers = System.Text.Json.JsonSerializer.Deserialize<List<SignerInput>>(signersJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new BadHttpRequestException("signersJson invalid");
        var dto = await _mediator.Send(new CreateAgreementCommand(title, pdf.OpenReadStream(), pdf.FileName, signers), ct);
        return Ok(dto);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _mediator.Send(new ListAgreementsQuery(), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await _mediator.Send(new GetAgreementQuery(id), ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet("{id:guid}/document")]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var path = await _mediator.Send(new GetAgreementDocumentPathQuery(id), ct);
        if (path is null) return NotFound();
        return PhysicalFile(path, "application/pdf", $"agreement-{id:N}.pdf");
    }

    [HttpPost("{id:guid}/signers")]
    public async Task<IActionResult> AddSigner(Guid id, AddSignerCommand command, CancellationToken ct)
    {
        await _mediator.Send(command with { AgreementId = id }, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/signers/{signerId:guid}")]
    public async Task<IActionResult> RemoveSigner(Guid id, Guid signerId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveSignerCommand(id, signerId), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CancelAgreementCommand(id), ct);
        return NoContent();
    }
}
