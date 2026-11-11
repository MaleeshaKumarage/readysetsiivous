using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Inventory;
using CleaningSuite.Domain.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/partner/inventory-acknowledgments")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "partner")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerInventoryController(InventoryAcknowledgmentService service) : ControllerBase
{
    private Guid Actor => (HttpContext.Items["Employee"] as Employee)?.Id
        ?? throw new UnauthorizedAccessException("A verified partner account is required.");

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool history = false, [FromQuery] int skip = 0, CancellationToken ct = default) =>
        Ok(await service.List(Actor, history, skip, ct));

    [HttpPost("{id:guid}/response")]
    public async Task<IActionResult> Respond(Guid id, InventoryAcknowledgmentResponse body, CancellationToken ct) =>
        Ok(await service.Respond(Actor, id, body, ct));

    [HttpGet("{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(Guid id, Guid photoId, CancellationToken ct)
    {
        var (bytes, contentType) = await service.Photo(Actor, id, photoId, ct);
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Disposition"] = "attachment";
        return File(bytes, contentType);
    }
}
