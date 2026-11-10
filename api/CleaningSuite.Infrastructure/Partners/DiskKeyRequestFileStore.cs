using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Infrastructure.Uploads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Partners;

/// <summary>Key request photos on the tenant's upload disk, one folder per request.</summary>
public sealed class DiskKeyRequestFileStore(IConfiguration config, ITenantContext tenant, ILogger<DiskKeyRequestFileStore> logger) : IKeyRequestFileStore
{
    private string PathFor(Guid requestId, KeyRequestPhoto photo)
    {
        if (photo.Extension is not (".jpg" or ".png" or ".webp")) throw new InvalidOperationException("Unsupported stored key request photo format.");
        var partition = TenantUploadPath.Partition(tenant.TenantId);
        return Path.Combine(TenantUploadPath.Root(config), "key-requests", partition, requestId.ToString("N"), photo.Id.ToString("N") + photo.Extension);
    }

    public async Task SaveAsync(Guid requestId, KeyRequestPhoto photo, byte[] content, CancellationToken ct)
    {
        var path = PathFor(requestId, photo);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            // Create overwrites a leftover from an earlier attempt, so a retried save does not fail.
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(content, ct);
        }
        catch
        {
            // A truncated file must never be read back as a valid photo.
            try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { logger.LogError(e, "Partial key request photo cleanup failed for {RequestId}/{PhotoId}", requestId, photo.Id); }
            throw;
        }
    }

    public async Task<byte[]?> ReadAsync(Guid requestId, KeyRequestPhoto photo, CancellationToken ct)
    {
        try { return await File.ReadAllBytesAsync(PathFor(requestId, photo), ct); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public Task DeleteAsync(Guid requestId, KeyRequestPhoto photo)
    {
        try { File.Delete(PathFor(requestId, photo)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { logger.LogError(e, "Key request photo cleanup failed for {RequestId}/{PhotoId}", requestId, photo.Id); }
        return Task.CompletedTask;
    }
}
