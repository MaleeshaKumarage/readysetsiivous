using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Employees;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Key requests assigned to the calling employee: complete them and add completion photos.</summary>
[ApiController]
[Route("api/v1/me/key-requests")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MeKeyRequestController(PartnerKeyRequestService keyRequests) : ControllerBase
{
    private Guid Actor => (HttpContext.Items["Employee"] as Employee)?.Id
        ?? throw new UnauthorizedAccessException("An active staff account is required.");

    [HttpGet] public async Task<IActionResult> Assigned(CancellationToken ct) => Ok(await keyRequests.Assigned(Actor, ct));
    [HttpPost("{id:guid}/complete")] public async Task<IActionResult> Complete(Guid id, KeyRequestRevision body, CancellationToken ct) => Ok(await keyRequests.Complete(Actor, id, body, ct));
    [RequestSizeLimit(10 * 1024 * 1024 + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024 + 65536)]
    [HttpPost("{id:guid}/photos")] public async Task<IActionResult> AddPhoto(Guid id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException([new("File", "Choose a photo to upload.")]);
        // Rejecting oversized files before reading keeps the buffer bounded at 10 MiB.
        if (file.Length > 10 * 1024 * 1024) throw new UploadPayloadTooLargeException("Key request photos must be 10 MiB or smaller.");
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return Ok(await keyRequests.AddPhoto(Actor, id, buffer.ToArray(), ct));
    }
    [HttpGet("{id:guid}/photos/{photoId:guid}")] public async Task<IActionResult> Photo(Guid id, Guid photoId, CancellationToken ct)
    {
        var photo = await keyRequests.Photo(Actor, id, photoId, ct);
        Response.Headers["Content-Disposition"] = "attachment";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(photo.Content, photo.ContentType);
    }
}
