using System.Text.RegularExpressions;
using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/leads")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class LeadsAdminController : ControllerBase
{
    private const int MaxTake = 200;
    private const int MaxFileBytes = 5 * 1024 * 1024;
    private static readonly Regex StorageNamePattern = new(
        "^[a-f0-9]{32}\\.(jpg|png|webp|pdf)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IMediator _mediator;
    private readonly ILeadRepository _leads;
    private readonly IConfiguration _configuration;
    private readonly IUploadContentValidator _uploads;

    public LeadsAdminController(
        IMediator mediator,
        ILeadRepository leads,
        IConfiguration configuration,
        IUploadContentValidator uploads)
    {
        _mediator = mediator;
        _leads = leads;
        _configuration = configuration;
        _uploads = uploads;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxTake);
        var result = await _mediator.Send(new ListLeadsQuery(search, skip, take), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLeadQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLeadCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLeadCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateLeadCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ReactivateLeadCommand(id), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteLeadCommand(id), ct);
        TryDeleteDirectory(LeadDirectory(id));
        return NoContent();
    }

    [HttpPost("{id:guid}/attachments")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new ValidationException([new ValidationFailure("File", "Choose a non-empty file.")]);
        if (file.Length > MaxFileBytes)
            throw new UploadPayloadTooLargeException("Each lead file must be 5 MiB or smaller.");

        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await input.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        var (extension, contentType) = Inspect(content);

        var directory = LeadDirectory(id);
        Directory.CreateDirectory(directory);
        var storageName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(directory, storageName);
        try
        {
            await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.WriteAsync(content, ct);

            var saved = await _mediator.Send(new AddLeadAttachmentCommand(
                id,
                SanitizeFileName(file.FileName),
                contentType,
                storageName,
                content.LongLength), ct);
            return Ok(saved);
        }
        catch
        {
            TryDeleteFile(fullPath);
            throw;
        }
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var lead = await _leads.GetAsync(id, ct) ?? throw new NotFoundException("Lead", id);
        var attachment = (lead.Attachments ?? []).FirstOrDefault(a => a.Id == attachmentId)
            ?? throw new NotFoundException("Lead attachment", attachmentId);
        if (string.IsNullOrWhiteSpace(attachment.StorageName) || !StorageNamePattern.IsMatch(attachment.StorageName))
            throw new NotFoundException("Lead attachment", attachmentId);

        var fullPath = Path.Combine(LeadDirectory(id), attachment.StorageName);
        if (!System.IO.File.Exists(fullPath))
            throw new NotFoundException("Lead attachment", attachmentId);

        return PhysicalFile(fullPath, attachment.ContentType, attachment.FileName);
    }

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var removed = await _mediator.Send(new RemoveLeadAttachmentCommand(id, attachmentId), ct);
        if (StorageNamePattern.IsMatch(removed.StorageName))
            TryDeleteFile(Path.Combine(LeadDirectory(id), removed.StorageName));
        return NoContent();
    }

    private (string Extension, string ContentType) Inspect(byte[] content)
    {
        if (content.Length >= 5 && content.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return (".pdf", "application/pdf");

        var extension = _uploads.ValidateServiceImage(content);
        var contentType = extension switch
        {
            ".jpg" => "image/jpeg",
            ".png" => "image/png",
            _ => "image/webp",
        };
        return (extension, contentType);
    }

    private string LeadDirectory(Guid id)
    {
        var root = Path.GetFullPath(_configuration["Uploads:Path"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads"));
        var directory = Path.GetFullPath(Path.Combine(root, "leads", id.ToString("N")));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid upload path.");
        return directory;
    }

    private static string SanitizeFileName(string? name)
    {
        var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(name) ? "file" : name.Trim());
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "file";
        return fileName.Length <= 180 ? fileName : fileName[..180];
    }

    private static void TryDeleteFile(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
