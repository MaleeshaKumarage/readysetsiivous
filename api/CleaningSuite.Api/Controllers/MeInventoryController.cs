using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Employee self-service inventory: pick a location and item, log a take with photos, see own history.</summary>
[ApiController]
[Route("api/v1/me/inventory")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "staff")]
public class MeInventoryController(InventoryService inventory) : ControllerBase
{
    [HttpGet("locations")]
    public async Task<IActionResult> Locations(CancellationToken ct) =>
        Ok(await inventory.ListLocationsAsync(includeInactive: false, ct));

    [HttpGet("items")]
    public async Task<IActionResult> Items(CancellationToken ct) =>
        Ok(await inventory.ListItemsAsync(includeInactive: false, ct));

    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] AddItemRequest body, CancellationToken ct) =>
        Ok(await inventory.AddItemFromEmployeeAsync(Actor(), body.Name, body.Unit, ct));

    [HttpGet("usage")]
    public async Task<IActionResult> OwnUsage([FromQuery] int take = 50, [FromQuery] int skip = 0, CancellationToken ct = default)
    {
        var employee = Actor();
        return Ok(await inventory.ListUsageAsync(employee.EmployeeId, null, null, Math.Max(0, skip), Math.Clamp(take, 1, 200), ct));
    }

    // Room for the maximum photos at the per-photo limit, plus form overhead. Anything larger is refused before it is read.
    [HttpPost("usage")]
    [RequestSizeLimit(27 * 1024 * 1024)]
    public async Task<IActionResult> RecordUsage(CancellationToken ct)
    {
        // The actor is resolved before the body is read. Model binding would buffer every uploaded file first,
        // so a caller with no employee record could otherwise make the server buffer a full upload.
        var actor = Actor();
        var form = await Request.ReadFormAsync(ct);
        if (!Guid.TryParse(form["LocationId"].ToString(), out var locationId) || !Guid.TryParse(form["ItemId"].ToString(), out var itemId))
            throw new FluentValidation.ValidationException("Choose a location and an item.");
        if (!int.TryParse(form["Quantity"].ToString(), out var quantity))
            throw new FluentValidation.ValidationException("Quantity must be a whole number.");
        var files = form.Files.GetFiles("Photos");
        if (files.Count > InventoryService.MaxPhotosPerEntry)
            throw new FluentValidation.ValidationException($"Attach up to {InventoryService.MaxPhotosPerEntry} photos per entry.");
        var photos = new List<byte[]>();
        foreach (var file in files)
        {
            if (file.Length == 0) throw new FluentValidation.ValidationException("Choose a non-empty photo.");
            if (file.Length > InventoryService.MaxPhotoBytes)
                throw new FluentValidation.ValidationException("Each photo must be 5 MiB or smaller.");
            await using var input = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, ct);
            photos.Add(buffer.ToArray());
        }
        return Ok(await inventory.RecordUsageAsync(actor, locationId, itemId, quantity, form["Note"].ToString(), photos, ct));
    }

    [HttpGet("usage/{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(Guid id, Guid photoId, CancellationToken ct)
    {
        var employee = Actor();
        var (bytes, contentType) = await inventory.ReadPhotoAsync(id, photoId, employee.EmployeeId, ct);
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Served as a download, so a stored file is never rendered inline in the application origin.
        Response.Headers["Content-Disposition"] = "attachment";
        return File(bytes, contentType);
    }

    private InventoryActor Actor()
    {
        var employee = StaffActor.RequireActive(HttpContext);
        return new InventoryActor(employee.Id, $"{employee.FirstName} {employee.LastName}".Trim());
    }

    public sealed record AddItemRequest(string Name, string? Unit);

}
