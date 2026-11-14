using CleaningSuite.Application.Inventory;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Inventory;
using CleaningSuite.Infrastructure.Uploads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Inventory;

/// <summary>Stores inventory photos on disk, partitioned by tenant and entry. Files are never served directly.</summary>
public sealed class DiskInventoryFileStore(IConfiguration config, ITenantContext tenant, ILogger<DiskInventoryFileStore> logger) : IInventoryFileStore
{
    private string PathFor(Guid usageId, InventoryPhoto photo)
    {
        if (photo.Extension is not (".jpg" or ".png" or ".webp")) throw new InventoryConflictException("Unsupported stored photo format.");
        return Path.Combine(TenantUploadPath.Root(config), "inventory", TenantUploadPath.Partition(tenant.TenantId),
            usageId.ToString("N"), photo.Id.ToString("N") + photo.Extension);
    }

    public async Task SaveAsync(Guid usageId, InventoryPhoto photo, byte[] content, CancellationToken ct)
    {
        var path = PathFor(usageId, photo);
        content = CleaningSuite.Infrastructure.Uploads.ImageCompressor.Compress(content, photo.Extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write beside the final file, then rename it into place. A cancelled write, crash or IO error leaves only
        // a temporary file, never a truncated photo at the final path, and a retry can still save the photo.
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(content, ct);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            DeleteQuietly(temp);
            throw;
        }
    }

    private void DeleteQuietly(string file)
    {
        try { File.Delete(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not remove temporary inventory photo {File}", file);
        }
    }

    public async Task<byte[]?> ReadAsync(Guid usageId, InventoryPhoto photo, CancellationToken ct)
    {
        try { return await File.ReadAllBytesAsync(PathFor(usageId, photo), ct); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public Task DeleteAsync(Guid usageId, InventoryPhoto photo)
    {
        try { File.Delete(PathFor(usageId, photo)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Inventory photo cleanup failed for {UsageId}/{PhotoId}", usageId, photo.Id);
        }
        return Task.CompletedTask;
    }
}
