using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Uploads;

/// <summary>Where tenant uploads live on disk. Shared by every disk-backed upload store so the layout stays identical.</summary>
public static class TenantUploadPath
{
    public static string Root(IConfiguration config) =>
        config["Uploads:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");

    /// <summary>A stable, filesystem-safe folder name for a tenant, so tenant ids never appear in paths.</summary>
    public static string Partition(string tenantId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenantId))).ToLowerInvariant();
}
