using CleaningSuite.Application.Agreements;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Agreements;

public class DiskAgreementFileStore : IAgreementFileStore
{
    private readonly string _root;
    private readonly ILogger<DiskAgreementFileStore>? _logger;

    public DiskAgreementFileStore(string root, ILogger<DiskAgreementFileStore>? logger = null)
    {
        _root = Path.GetFullPath(root);
        _logger = logger;
    }

    private string Dir(string tenantId, Guid agreementId)
    {
        var dir = Path.Combine(_root, "agreements", tenantId, agreementId.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> SaveOriginalAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir(tenantId, agreementId), $"original-{Guid.NewGuid():N}.pdf");
        try
        {
            await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await pdf.CopyToAsync(fs, ct);
            return path;
        }
        catch { TryDeletePartial(path, tenantId, agreementId); throw; }
    }

    public async Task<string> SaveSignatureAsync(string tenantId, Guid agreementId, Guid signerId, byte[] png, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir(tenantId, agreementId), $"{signerId:N}-{Guid.NewGuid():N}.png");
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(png, ct);
            return path;
        }
        catch { TryDeletePartial(path, tenantId, agreementId); throw; }
    }

    public async Task<string> SaveSignedAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir(tenantId, agreementId), $"signed-{Guid.NewGuid():N}.pdf");
        try
        {
            await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await pdf.CopyToAsync(fs, ct);
            return path;
        }
        catch { TryDeletePartial(path, tenantId, agreementId); throw; }
    }

    public Task DeleteArtifactAsync(string tenantId, Guid agreementId, string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var expectedDirectory = Path.GetFullPath(Dir(tenantId, agreementId)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Agreement artifact path is outside its tenant agreement directory.");
        try { if (File.Exists(fullPath)) File.Delete(fullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Could not remove unreferenced agreement artifact for tenant {TenantId} and agreement {AgreementId}.", tenantId, agreementId);
        }
        return Task.CompletedTask;
    }

    private void TryDeletePartial(string path, string tenantId, Guid agreementId)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Could not remove incomplete agreement artifact for tenant {TenantId} and agreement {AgreementId}.", tenantId, agreementId);
        }
    }
}
