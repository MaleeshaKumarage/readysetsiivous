using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Services.Commands;
using CleaningSuite.Application.Services.Queries;
using CleaningSuite.Application.Services;
using CleaningSuite.Application.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace CleaningSuite.Api.Controllers;

/// <summary>Service catalog management for the tenant admin.</summary>
[ApiController]
[Route("api/v1/admin/services")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class ServicesAdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;
    private readonly IServiceRepository _services;
    private readonly IUploadContentValidator _uploadValidator;

    public ServicesAdminController(IMediator mediator, IConfiguration configuration, IServiceRepository services, IUploadContentValidator uploadValidator)
    {
        _mediator = mediator;
        _configuration = configuration;
        _services = services;
        _uploadValidator = uploadValidator;
    }

    [HttpGet]
    [OutputCache(Duration = 30)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var services = await _mediator.Send(new ListServicesQuery(includeInactive), ct);
        return Ok(services);
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateServiceCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, new { id });
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateServiceCommand command, CancellationToken ct)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteServiceCommand(id), ct);
        return NoContent();
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new SetServiceActivityCommand(id, true), ct);
        return NoContent();
    }

    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new SetServiceActivityCommand(id, false), ct);
        return NoContent();
    }

    /// <summary>Uploads a card image for the service, returns the public path.</summary>
    [Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
    [HttpPost("{id:guid}/image")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        _ = await _services.GetByIdAsync(id, ct) ?? throw new NotFoundException("Service", id);
        if (file is null || file.Length == 0)
            throw new FluentValidation.ValidationException("Choose a non-empty service image.");
        if (file.Length > 5 * 1024 * 1024)
            throw new UploadPayloadTooLargeException("Service image must be 5 MiB or smaller.");

        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await input.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        var ext = _uploadValidator.ValidateServiceImage(content);

        var uploadsPath = _configuration["Uploads:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        Directory.CreateDirectory(uploadsPath);

        var name = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsPath, name);
        var imageUrl = $"/uploads/{name}";
        try
        {
            await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.WriteAsync(content, ct);
            await _mediator.Send(new UpdateServiceImageCommand(id, imageUrl), ct);
        }
        catch
        {
            try { if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            { /* Keep the original failure; orphan cleanup can be handled separately. */ }
            throw;
        }
        return Ok(new { imageUrl });
    }
}
