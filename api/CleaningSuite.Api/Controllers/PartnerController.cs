using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/partner")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "partner")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerController(PartnerService service) : ControllerBase
{
    private Guid Actor => (HttpContext.Items["Employee"] as Employee)?.Id
        ?? throw new UnauthorizedAccessException("A verified partner account is required.");
    [HttpGet("me")] public async Task<IActionResult> Me(CancellationToken ct) => Ok(await service.Me(Actor, ct));
    [HttpGet("branches")] public async Task<IActionResult> Branches(CancellationToken ct) => Ok(await service.Branches(Actor, false, null, ct));
    [HttpPost("branches")] public async Task<IActionResult> CreateBranch(CreatePartnerBranch body, CancellationToken ct) => Ok(await service.CreateBranch(Actor, body, ct));
    [HttpPost("branches/{id:guid}/quote-request")] public async Task<IActionResult> Quote(Guid id, CancellationToken ct) => Ok(await service.Quote(Actor, id, ct));
    [HttpPost("branches/{id:guid}/change-requests")] public async Task<IActionResult> PropertyChange(Guid id, PartnerMessage body, CancellationToken ct) => Ok(await service.Change(Actor, id, true, body, ct));
    [HttpGet("requests")] public async Task<IActionResult> Requests(CancellationToken ct) => Ok(await service.Requests(Actor, false, null, ct));
    [HttpGet("change-requests")] public async Task<IActionResult> Changes(CancellationToken ct) => Ok(await service.OwnChanges(Actor, ct));
    [HttpGet("requests/{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await service.Get(Actor, id, ct));
    [HttpPost("requests")] public async Task<IActionResult> Create(CreatePartnerRequest body, CancellationToken ct) => Ok(await service.Create(Actor, body, ct));
    [HttpPut("requests/{id:guid}")] public async Task<IActionResult> Edit(Guid id, EditPartnerRequest body, CancellationToken ct) => Ok(await service.Edit(Actor, id, body, ct));
    [HttpPost("requests/{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, PartnerVersion body, CancellationToken ct) => Ok(await service.Cancel(Actor, id, body.Version, false, null, ct));
    [HttpPost("requests/{id:guid}/change-requests")] public async Task<IActionResult> Change(Guid id, PartnerMessage body, CancellationToken ct) => Ok(await service.Change(Actor, id, false, body, ct));
    [HttpGet("shifts")] public async Task<IActionResult> Shifts(CancellationToken ct) => Ok(await service.CompanyShifts(Actor, ct));
}
