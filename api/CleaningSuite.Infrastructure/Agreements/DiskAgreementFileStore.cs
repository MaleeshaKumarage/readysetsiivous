using CleaningSuite.Application.Agreements;

namespace CleaningSuite.Infrastructure.Agreements;

public class DiskAgreementFileStore : IAgreementFileStore
{
    private readonly string _root;

    public DiskAgreementFileStore(string root) => _root = Path.GetFullPath(root);

    private string Dir(string tenantId, Guid agreementId)
    {
        var dir = Path.Combine(_root, "agreements", tenantId, agreementId.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> SaveOriginalAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir(tenantId, agreementId), "original.pdf");
        await using var fs = File.Create(path);
        await pdf.CopyToAsync(fs, ct);
        return path;
    }

    public async Task<string> SaveSignatureAsync(string tenantId, Guid agreementId, Guid signerId, byte[] png, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir(tenantId, agreementId), $"{signerId:N}.png");
        await File.WriteAllBytesAsync(path, png, ct);
        return path;
    }

    public async Task<string> SaveSignedAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default)
    {
        var path = Path.Combine(Dir(tenantId, agreementId), "signed.pdf");
        await using var fs = File.Create(path);
        await pdf.CopyToAsync(fs, ct);
        return path;
    }
}
