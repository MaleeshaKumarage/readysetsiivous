using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Services.Commands;
using CleaningSuite.Application.Services.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Service catalog management for the tenant admin.</summary>
[ApiController]
[Route("api/v1/admin/services")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class ServicesAdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public ServicesAdminController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var services = await _mediator.Send(new ListServicesQuery(includeInactive), ct);
        return Ok(services);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateServiceCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, new { id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateServiceCommand command, CancellationToken ct)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new SoftDeleteServiceCommand(id), ct);
        return NoContent();
    }

    /// <summary>Uploads a card image for the service, returns the public path.</summary>
    [HttpPost("{id:guid}/image")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > 5 * 1024 * 1024)
            return BadRequest(new { title = "Image must be under 5 MB" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            return BadRequest(new { title = "Only jpg, png or webp allowed" });

        var uploadsPath = _configuration["Uploads:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        Directory.CreateDirectory(uploadsPath);

        var name = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsPath, name);
        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var imageUrl = $"/uploads/{name}";
        await _mediator.Send(new UpdateServiceImageCommand(id, imageUrl), ct);
        return Ok(new { imageUrl });
    }
}
