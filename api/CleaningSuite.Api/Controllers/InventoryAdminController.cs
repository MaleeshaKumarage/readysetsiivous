using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Admin inventory: manage locations and items, review every employee's take log by date, export CSV.</summary>
[ApiController]
[Route("api/v1/admin/inventory")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InventoryAdminController(InventoryService inventory, InventoryAcknowledgmentService acknowledgments) : ControllerBase
{
    [HttpGet("acknowledgments/partners")]
    public async Task<IActionResult> Partners([FromQuery] string? search, CancellationToken ct) =>
        Ok(await acknowledgments.Partners(search, ct));

    [HttpGet("acknowledgments")]
    public async Task<IActionResult> Acknowledgments([FromQuery] Guid[]? usageIds, CancellationToken ct) =>
        Ok(await acknowledgments.Lookup(usageIds ?? [], ct));

    [HttpPost("acknowledgments")]
    public async Task<IActionResult> RequestAcknowledgment(InventoryAcknowledgmentInput body, CancellationToken ct) =>
        Ok(await acknowledgments.Create(User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException("An authenticated admin identity is required."), body, ct));

    [HttpGet("locations")]
    public async Task<IActionResult> Locations([FromQuery] bool includeInactive = true, CancellationToken ct = default) =>
        Ok(await inventory.ListLocationsAsync(includeInactive, ct));

    [HttpPost("locations")]
    public async Task<IActionResult> CreateLocation([FromBody] CreateNameRequest body, CancellationToken ct) =>
        Ok(await inventory.CreateLocationAsync(body.Name, ct));

    [HttpPut("locations/{id:guid}")]
    public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] UpdateLocationRequest body, CancellationToken ct) =>
        Ok(await inventory.UpdateLocationAsync(id, body.Name, body.IsActive, body.Version, ct));

    [HttpGet("items")]
    public async Task<IActionResult> Items([FromQuery] bool includeInactive = true, CancellationToken ct = default) =>
        Ok(await inventory.ListItemsAsync(includeInactive, ct));

    [HttpPost("items")]
    public async Task<IActionResult> CreateItem([FromBody] CreateItemRequest body, CancellationToken ct) =>
        Ok(await inventory.CreateItemAsync(body.Name, body.Unit, ct));

    [HttpPut("items/{id:guid}")]
    public async Task<IActionResult> UpdateItem(Guid id, [FromBody] UpdateItemRequest body, CancellationToken ct) =>
        Ok(await inventory.UpdateItemAsync(id, body.Name, body.Unit, body.IsActive, body.Version, ct));

    [HttpPost("items/{id:guid}/verify")]
    public async Task<IActionResult> VerifyItem(Guid id, [FromBody] VersionRequest body, CancellationToken ct) =>
        Ok(await inventory.VerifyItemAsync(id, body.Version, ct));

    [HttpGet("usage")]
    public async Task<IActionResult> Usage([FromQuery] Guid? employeeId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int take = 100, [FromQuery] int skip = 0, CancellationToken ct = default) =>
        Ok(await inventory.ListUsageAsync(employeeId, from, to, Math.Max(0, skip), Math.Clamp(take, 1, 200), ct));

    [HttpGet("usage/export")]
    public async Task<IActionResult> Export([FromQuery] Guid? employeeId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var csv = await inventory.ExportCsvAsync(employeeId, from, to, ct);
        Response.Headers["Cache-Control"] = "no-store";
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8",
            $"inventory-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    [HttpGet("usage/{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(Guid id, Guid photoId, CancellationToken ct)
    {
        var (bytes, contentType) = await inventory.ReadPhotoAsync(id, photoId, null, ct);
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Served as a download, so a stored file is never rendered inline in the application origin.
        Response.Headers["Content-Disposition"] = "attachment";
        return File(bytes, contentType);
    }

    public sealed record VersionRequest(Guid Version);
    public sealed record CreateNameRequest(string Name);
    public sealed record UpdateLocationRequest(string Name, bool IsActive, Guid Version);
    public sealed record CreateItemRequest(string Name, string? Unit);
    public sealed record UpdateItemRequest(string Name, string? Unit, bool IsActive, Guid Version);
}
