using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Application.Common;

public interface IUploadContentValidator
{
    void ValidateAgreementPdf(ReadOnlyMemory<byte> content);
    void ValidateGeneratedAgreementPdf(ReadOnlyMemory<byte> content);
    void ValidateSignaturePng(ReadOnlyMemory<byte> content);
    string ValidateServiceImage(ReadOnlyMemory<byte> content);
    int ValidateQualityCyclePhotos(IReadOnlyList<QualityCyclePhoto>? photos, IReadOnlyList<string>? photoUrls);
}
