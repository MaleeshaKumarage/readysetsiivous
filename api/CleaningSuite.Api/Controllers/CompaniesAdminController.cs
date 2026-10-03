using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Companies;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/companies")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class CompaniesAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public CompaniesAdminController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListCompaniesQuery(search, skip, take), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCompanyQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompanyCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyCommand command, CancellationToken ct)
    {
        if (id != command.Id)
            return BadRequest("Company id mismatch.");
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateCompanyCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{companyId:guid}/branches")]
    public async Task<IActionResult> CreateBranch(Guid companyId, [FromBody] CreateBranchCommand command, CancellationToken ct)
    {
        if (companyId != command.CompanyId)
            return BadRequest("Company id mismatch.");
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPut("branches/{branchId:guid}")]
    public async Task<IActionResult> UpdateBranch(Guid branchId, [FromBody] UpdateBranchCommand command, CancellationToken ct)
    {
        if (branchId != command.Id)
            return BadRequest("Branch id mismatch.");
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("branches/{branchId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateBranch(Guid branchId, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateBranchCommand(branchId), ct);
        return NoContent();
    }
}
