using CleaningSuite.Domain.Agreements;

namespace CleaningSuite.Application.Agreements;

public interface IAgreementDocumentGenerator
{
    Task GenerateSignedPdfAsync(string originalPdfPath, IReadOnlyList<Signer> signers,
        string outputPath, CancellationToken ct = default);
}
