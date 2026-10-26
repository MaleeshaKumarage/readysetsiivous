using CleaningSuite.Application.Common;
using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Tests.Support;

internal sealed class NoopUploadContentValidator : IUploadContentValidator
{
    public static NoopUploadContentValidator Instance { get; } = new();
    public Task ValidateAgreementPdfAsync(ReadOnlyMemory<byte> content, CancellationToken ct = default) => Task.CompletedTask;
    public Task ValidateGeneratedAgreementPdfAsync(ReadOnlyMemory<byte> content, CancellationToken ct = default) => Task.CompletedTask;
    public void ValidateSignaturePng(ReadOnlyMemory<byte> content) { }
    public string ValidateServiceImage(ReadOnlyMemory<byte> content) => ".png";
    public int ValidateQualityCyclePhotos(IReadOnlyList<QualityCyclePhoto>? photos, IReadOnlyList<string>? photoUrls) => 0;
}
