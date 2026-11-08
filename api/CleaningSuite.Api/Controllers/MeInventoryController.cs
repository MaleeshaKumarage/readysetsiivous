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
    public async Task<IActionResult> RecordUsage([FromForm] MeUsageForm form, CancellationToken ct)
    {
        // Authorise before reading any file, so a non-staff caller cannot make the server buffer uploads.
        var actor = Actor();
        if ((form.Photos?.Count ?? 0) > InventoryService.MaxPhotosPerEntry)
            throw new FluentValidation.ValidationException($"Attach up to {InventoryService.MaxPhotosPerEntry} photos per entry.");
        var photos = new List<byte[]>();
        foreach (var file in form.Photos ?? [])
        {
            if (file.Length == 0) throw new FluentValidation.ValidationException("Choose a non-empty photo.");
            if (file.Length > InventoryService.MaxPhotoBytes)
                throw new CleaningSuite.Application.Common.UploadPayloadTooLargeException("Inventory photos must be 5 MiB or smaller.");
            await using var input = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, ct);
            photos.Add(buffer.ToArray());
        }
        return Ok(await inventory.RecordUsageAsync(actor, form.LocationId, form.ItemId, form.Quantity, form.Note, photos, ct));
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

    public sealed class MeUsageForm
    {
        public Guid LocationId { get; set; }
        public Guid ItemId { get; set; }
        public int Quantity { get; set; }
        public string? Note { get; set; }
        public List<IFormFile>? Photos { get; set; }
    }
}
