namespace CleaningSuite.Application.Agreements;

public interface IAgreementFileStore
{
    Task<string> SaveOriginalAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default);
    Task<string> SaveSignatureAsync(string tenantId, Guid agreementId, Guid signerId, byte[] png, CancellationToken ct = default);
    Task<string> SaveSignedAsync(string tenantId, Guid agreementId, Stream pdf, CancellationToken ct = default);
}
