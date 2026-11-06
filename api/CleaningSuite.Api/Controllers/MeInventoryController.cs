using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Inventory;
using CleaningSuite.Domain.Employees;
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

    [HttpPost("usage")]
    [RequestSizeLimit(40 * 1024 * 1024)]
    public async Task<IActionResult> RecordUsage([FromForm] MeUsageForm form, CancellationToken ct)
    {
        var photos = new List<byte[]>();
        foreach (var file in form.Photos ?? [])
        {
            if (file.Length == 0) throw new FluentValidation.ValidationException("Choose a non-empty photo.");
            await using var input = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, ct);
            photos.Add(buffer.ToArray());
        }
        return Ok(await inventory.RecordUsageAsync(Actor(), form.LocationId, form.ItemId, form.Quantity, form.Note, photos, ct));
    }

    [HttpGet("usage/{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(Guid id, Guid photoId, CancellationToken ct)
    {
        var employee = Actor();
        var (bytes, contentType) = await inventory.ReadPhotoAsync(id, photoId, employee.EmployeeId, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(bytes, contentType);
    }

    private InventoryActor Actor()
    {
        var employee = HttpContext.Items["Employee"] as Employee;
        if (employee is null || !employee.IsActive || employee.IdentitySyncPending || !Employee.IsWorkRole(employee.Role)
            || string.IsNullOrWhiteSpace(employee.KeycloakUserId))
            throw new UnauthorizedAccessException("An active linked staff account is required.");
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
