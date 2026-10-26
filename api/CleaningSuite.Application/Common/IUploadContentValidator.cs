using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Application.Common;

public interface IUploadContentValidator
{
    Task ValidateAgreementPdfAsync(ReadOnlyMemory<byte> content, CancellationToken ct = default);
    Task ValidateGeneratedAgreementPdfAsync(ReadOnlyMemory<byte> content, CancellationToken ct = default);
    void ValidateSignaturePng(ReadOnlyMemory<byte> content);
    string ValidateServiceImage(ReadOnlyMemory<byte> content);
    int ValidateQualityCyclePhotos(IReadOnlyList<QualityCyclePhoto>? photos, IReadOnlyList<string>? photoUrls);
}
