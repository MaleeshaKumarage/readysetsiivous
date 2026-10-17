using CleaningSuite.Application.Common;
using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Tests.Support;

internal sealed class NoopUploadContentValidator : IUploadContentValidator
{
    public static NoopUploadContentValidator Instance { get; } = new();
    public void ValidateAgreementPdf(ReadOnlyMemory<byte> content) { }
    public void ValidateGeneratedAgreementPdf(ReadOnlyMemory<byte> content) { }
    public void ValidateSignaturePng(ReadOnlyMemory<byte> content) { }
    public string ValidateServiceImage(ReadOnlyMemory<byte> content) => ".png";
    public int ValidateQualityCyclePhotos(IReadOnlyList<QualityCyclePhoto>? photos, IReadOnlyList<string>? photoUrls) => 0;
}
