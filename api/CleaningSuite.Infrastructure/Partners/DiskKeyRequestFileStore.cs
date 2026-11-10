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
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(content, ct);
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
