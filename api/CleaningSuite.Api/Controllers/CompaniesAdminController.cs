using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Companies;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/companies")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class CompaniesAdminController : ControllerBase
{
    private const int MaxTake = 200;
    // The company list is cached for 30 seconds; every change to companies or their shifts and places evicts it so reloads never show stale rows.
    private const string CompaniesCacheTag = "companies";
    // Shift lists are cached too, so a cleanup that removes shifts or places must evict them.
    private const string ShiftsCacheTag = "shifts";

    private readonly IMediator _mediator;
    private readonly IOutputCacheStore _outputCache;

    public CompaniesAdminController(IMediator mediator, IOutputCacheStore outputCache)
    {
        _mediator = mediator;
        _outputCache = outputCache;
    }

    // Eviction uses CancellationToken.None: a client that disconnects after a commit must not leave the cache stale.
    private ValueTask EvictCompanyCachesAsync() => _outputCache.EvictByTagAsync(CompaniesCacheTag, CancellationToken.None);

    [HttpGet]
    [OutputCache(Duration = 30, Tags = new[] { CompaniesCacheTag })]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var normalizedStatus = (status ?? "all").Trim().ToLowerInvariant();
        if (normalizedStatus is not ("all" or "active" or "inactive"))
        {
            return BadRequest(new { error = "status must be all, active or inactive." });
        }

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxTake);
        var result = await _mediator.Send(new ListCompaniesQuery(search, skip, take, normalizedStatus), ct);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new CompanySummaryQuery(), ct);
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
        await EvictCompanyCachesAsync();
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        await EvictCompanyCachesAsync();
        return Ok(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateCompanyCommand(id), ct);
        await EvictCompanyCachesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ReactivateCompanyCommand(id), ct);
        await EvictCompanyCachesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    { await _mediator.Send(new DeleteCompanyCommand(id), ct); await EvictCompanyCachesAsync(); return NoContent(); }

    [HttpPost("{id:guid}/clear-related")]
    public async Task<IActionResult> ClearRelated(Guid id, [FromQuery] bool deleteSignedAgreements, [FromQuery] bool deleteCompany, CancellationToken ct)
    {
        try
        {
            var result = await _mediator.Send(new ClearCompanyRelatedCommand(id, deleteSignedAgreements, deleteCompany), ct);
            return Ok(result);
        }
        finally
        {
            // Runs on failure too: a clear that stopped part way has already removed some shifts and places.
            await EvictCompanyCachesAsync();
            await _outputCache.EvictByTagAsync(ShiftsCacheTag, CancellationToken.None);
        }
    }

    [HttpPost("branches/{branchId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateBranch(Guid branchId, CancellationToken ct)
    {
        await _mediator.Send(new ReactivateBranchCommand(branchId), ct);
        await EvictCompanyCachesAsync();
        return NoContent();
    }

    [HttpDelete("branches/{branchId:guid}")]
    public async Task<IActionResult> DeleteBranch(Guid branchId, CancellationToken ct)
    { await _mediator.Send(new DeleteBranchCommand(branchId), ct); await EvictCompanyCachesAsync(); return NoContent(); }

    [HttpPost("{companyId:guid}/branches")]
    public async Task<IActionResult> CreateBranch(Guid companyId, [FromBody] CreateBranchCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { CompanyId = companyId }, ct);
        await EvictCompanyCachesAsync();
        return Ok(result);
    }

    [HttpPut("branches/{branchId:guid}")]
    public async Task<IActionResult> UpdateBranch(Guid branchId, [FromBody] UpdateBranchCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { Id = branchId }, ct);
        await EvictCompanyCachesAsync();
        return Ok(result);
    }

    [HttpPost("branches/{branchId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateBranch(Guid branchId, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateBranchCommand(branchId), ct);
        await EvictCompanyCachesAsync();
        return NoContent();
    }
}
