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
        var signers = System.Text.Json.JsonSerializer.Deserialize<List<SignerInput>>(signersJson)
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
}
