using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Application.Common;

public interface IUploadContentValidator
{
    void ValidateAgreementPdf(ReadOnlyMemory<byte> content);
    void ValidateGeneratedAgreementPdf(ReadOnlyMemory<byte> content);
    void ValidateSignaturePng(ReadOnlyMemory<byte> content);
    string ValidateServiceImage(ReadOnlyMemory<byte> content);
    // Key request photos: JPEG, PNG or WebP up to 10 MiB. Returns the stored file extension.
    string ValidateKeyRequestPhoto(ReadOnlyMemory<byte> content);
    int ValidateQualityCyclePhotos(IReadOnlyList<QualityCyclePhoto>? photos, IReadOnlyList<string>? photoUrls);
}
