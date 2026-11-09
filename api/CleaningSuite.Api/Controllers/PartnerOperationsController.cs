using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Employees;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/partner")]
[Route("api/v1/manager/partner")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "operations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerOperationsController(PartnerService service, PartnerKeyRequestService keyRequests, IMediator mediator) : ControllerBase
{
    // The guard permits existing unbound tenant administrators, but denies all unbound managers and partners.
    private Guid Actor => (HttpContext.Items["Employee"] as Employee)?.Id ?? Guid.Empty;
    [HttpGet("requests")] public async Task<IActionResult> Requests([FromQuery] Guid? companyId, CancellationToken ct) => Ok(await service.Requests(Actor, true, companyId, ct));
    [HttpGet("quotes")] public async Task<IActionResult> Quotes(CancellationToken ct) => Ok(await service.Quotes(Actor, ct));
    [HttpGet("key-requests")] public async Task<IActionResult> KeyRequests([FromQuery] Guid? companyId, CancellationToken ct) => Ok(await keyRequests.List(Actor, true, companyId, ct));
    [HttpPut("key-request-fee")] public async Task<IActionResult> KeyRequestFee(KeyRequestFeeInput body, CancellationToken ct) => Ok(await keyRequests.SetFee(Actor, body, ct));
    [HttpGet("branches")] public async Task<IActionResult> Branches([FromQuery] Guid? companyId, CancellationToken ct) => Ok(await service.Branches(Actor, true, companyId, ct));
    [HttpPost("branches")] public async Task<IActionResult> CreateBranch(CreateBranchCommand body, CancellationToken ct) => Ok(await mediator.Send(body, ct));
    [HttpPut("branches/{id:guid}")] public async Task<IActionResult> UpdateBranch(Guid id, UpdateBranchCommand body, CancellationToken ct) => Ok(await mediator.Send(body with { Id = id }, ct));
    [HttpPut("branches/{id:guid}/pricing")] public async Task<IActionResult> Price(Guid id, PartnerPricing body, CancellationToken ct) => Ok(await service.Price(Actor, id, body, ct));
    [HttpGet("change-requests")] public async Task<IActionResult> Changes(CancellationToken ct) => Ok(await service.Changes(Actor, ct));
    [HttpPost("change-requests/{id:guid}/resolve")] public async Task<IActionResult> Resolve(Guid id, PartnerResolution body, CancellationToken ct) => Ok(await service.Resolve(Actor, id, body, ct));
    [HttpPost("change-requests/{id:guid}/apply")] public async Task<IActionResult> Apply(Guid id, PartnerChangeApply body, CancellationToken ct) => Ok(await service.Apply(Actor, id, body, ct));
    [HttpPost("branches/{id:guid}/quote-rejection")] public async Task<IActionResult> RejectQuote(Guid id, PartnerQuoteRejection body, CancellationToken ct) => Ok(await service.RejectQuote(Actor, id, body, ct));
    [HttpPost("requests/{id:guid}/offer")] public async Task<IActionResult> Offer(Guid id, PartnerOffer body, CancellationToken ct) => Ok(await service.Offer(Actor, id, body, ct));
    [HttpPut("requests/{id:guid}")] public async Task<IActionResult> Edit(Guid id, EditPartnerRequest body, CancellationToken ct) => Ok(await service.StaffEdit(Actor, id, body, ct));
    [HttpPost("requests/{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, PartnerCancellation body, CancellationToken ct) => Ok(await service.Cancel(Actor, id, body.Version, true, body.Reason, ct));
}

[ApiController]
[Route("api/v1/me/partner-offers")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerOffersController(PartnerService service) : ControllerBase
{
    private Guid Actor => (HttpContext.Items["Employee"] as Employee)?.Id
        ?? throw new UnauthorizedAccessException("An active linked staff account is required.");
    [HttpGet] public async Task<IActionResult> Offers(CancellationToken ct) => Ok(await service.Offers(Actor, ct));
    [HttpPost("{id:guid}/accept")] public async Task<IActionResult> Accept(Guid id, PartnerAcceptance body, CancellationToken ct)
    {
        var result = await service.Accept(Actor, id, body, ct);
        return Ok(new { result.Id, result.Version, result.Status, result.LinkedShiftId, result.PlannedStartUtc, result.PlannedEndUtc });
    }
    [HttpPost("{id:guid}/decline")] public async Task<IActionResult> Decline(Guid id, PartnerDecline body, CancellationToken ct) { await service.Decline(Actor, id, body, ct); return NoContent(); }
}
