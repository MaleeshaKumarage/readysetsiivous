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
    private const int MaxTake = 200;

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
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxTake);
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
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateCompanyCommand(id), ct);
        return NoContent();
    }

    [HttpGet("{companyId:guid}/branches")]
    public async Task<IActionResult> ListBranches(Guid companyId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListBranchesQuery(companyId), ct);
        return Ok(result);
    }

    [HttpPost("{companyId:guid}/branches")]
    public async Task<IActionResult> CreateBranch(Guid companyId, [FromBody] CreateBranchCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { CompanyId = companyId }, ct);
        return Ok(result);
    }

    [HttpPut("branches/{branchId:guid}")]
    public async Task<IActionResult> UpdateBranch(Guid branchId, [FromBody] UpdateBranchCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { Id = branchId }, ct);
        return Ok(result);
    }

    [HttpPost("branches/{branchId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateBranch(Guid branchId, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateBranchCommand(branchId), ct);
        return NoContent();
    }
}
