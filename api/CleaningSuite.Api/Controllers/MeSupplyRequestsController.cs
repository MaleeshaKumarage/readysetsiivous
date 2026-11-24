using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Employee supply requests: ask for catalogue items and see the status of your own requests.</summary>
[ApiController]
[Route("api/v1/me/supply-requests")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "staff")]
public class MeSupplyRequestsController(SupplyRequestService requests) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Own(CancellationToken ct) =>
        Ok(await requests.ListOwnAsync(Actor(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplyRequestBody body, CancellationToken ct) =>
        Ok(await requests.CreateAsync(
            Actor(),
            (body.Lines ?? []).Select(line => new SupplyRequestLineInput(line.ItemId, line.Quantity)).ToList(),
            body.Note,
            ct));

    private InventoryActor Actor()
    {
        var employee = StaffActor.RequireActive(HttpContext);
        return new InventoryActor(employee.Id, $"{employee.FirstName} {employee.LastName}".Trim());
    }

    public sealed record CreateSupplyRequestBody(List<SupplyLineBody>? Lines, string? Note);
    public sealed record SupplyLineBody(Guid ItemId, int Quantity);
}
