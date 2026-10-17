using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using FluentValidation;
using JasperFx;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record SignAgreementCommand(string Token, string TypedName, byte[] SignaturePng) : IRequest<SignResult>;

public record SignResult(bool Completed, int TotalSigners, int SignedCount);

public class SignAgreementValidator : AbstractValidator<SignAgreementCommand>
{
    private const int MaxSignatureBytes = 2 * 1024 * 1024; // 2 MB
    private const int MaxSignatureWidth = 1200;
    private const int MaxSignatureHeight = 400;

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public SignAgreementValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.TypedName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SignaturePng)
            .NotEmpty()
            .Must(BeReasonableSize).WithMessage("Signature must be at most 2 MB")
            .Must(BePng).WithMessage("Signature must be a valid PNG image")
            .Must(BeReasonableDimensions).WithMessage("Signature must be at most 1200x400 pixels");
    }

    // Size cap guards against base64 blobs and decompression bombs. Null-tolerant so a
    // null payload defers to the `.NotEmpty()` rule (400) instead of NREing in a Must.
    private static bool BeReasonableSize(byte[] png) => png is null || png.Length <= MaxSignatureBytes;

    // Verify the 8-byte PNG signature so a non-PNG cannot reach SKBitmap.Decode.
    private static bool BePng(byte[] png) =>
        png is null || (png.Length >= 8 && png.AsSpan(0, 8).SequenceEqual(PngMagic));

    // Cheap dimension check straight from the PNG IHDR (bytes 16-23), no decode needed.
    // A valid header with an enormous width/height would otherwise decompress into a bomb.
    private static bool BeReasonableDimensions(byte[] png)
    {
        if (png is null) return true;
        if (png.Length < 24) return false;
        var width = (uint)(png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19]);
        var height = (uint)(png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23]);
        return width <= MaxSignatureWidth && height <= MaxSignatureHeight;
    }
}

public class SignAgreementHandler : IRequestHandler<SignAgreementCommand, SignResult>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    private readonly IAgreementFileStore _store;
    private readonly IAgreementDocumentGenerator _generator;
    private readonly IUploadContentValidator _uploads;

    public SignAgreementHandler(ITenantContext context, IAgreementRepository repo,
        IAgreementFileStore store, IAgreementDocumentGenerator generator, IUploadContentValidator uploads)
    {
        _context = context; _repo = repo; _store = store; _generator = generator; _uploads = uploads;
    }

    public async Task<SignResult> Handle(SignAgreementCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;
        _uploads.ValidateSignaturePng(request.SignaturePng);

        var agreement = await _repo.FindBySignerTokenAsync(tenantId, request.Token, ct)
            ?? throw new NotFoundException("Agreement", Guid.Empty);
        var signer = agreement.Signers.FirstOrDefault(s => s.Token == request.Token)
            ?? throw new NotFoundException("Signer", Guid.Empty);

        if (agreement.Status == Agreement.StatusCancelled)
            throw new AgreementConflictException("Agreement cancelled");
        if (!agreement.IsActive)
            throw new AgreementConflictException("Agreement deactivated");
        if (signer.Status == Signer.StatusSigned)
            throw new AgreementConflictException("Already signed");

        string? signaturePath = null;
        string? signedPdfPath = null;
        try
        {
            signaturePath = await _store.SaveSignatureAsync(tenantId, agreement.Id, signer.Id, request.SignaturePng, ct);

            // Marten concurrency serializes signer updates. Keep each attempt's files
            // immutable and retry only while this signer remains pending.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        agreement = await _repo.ReloadBySignerTokenAsync(tenantId, request.Token, ct)
                            ?? throw new NotFoundException("Agreement", Guid.Empty);
                        signer = agreement.Signers.FirstOrDefault(s => s.Token == request.Token)
                            ?? throw new NotFoundException("Signer", Guid.Empty);

                        EnsureCanSign(agreement, signer);
                    }

                    signer.SignatureImagePath = signaturePath;
                    signer.TypedName = request.TypedName;
                    signer.SignedAtUtc = DateTime.UtcNow;
                    signer.Status = Signer.StatusSigned;

                    var signedCount = agreement.Signers.Count(s => s.Status == Signer.StatusSigned);
                    var total = agreement.Signers.Count;

                    if (agreement.AllSigned)
                    {
                        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
                        try
                        {
                            await _generator.GenerateSignedPdfAsync(agreement.OriginalPdfPath, agreement.Signers, tempPath, ct);
                            await using var generated = File.OpenRead(tempPath);
                            var pdfBytes = await BoundedStreamReader.ReadAsync(generated, 100 * 1024 * 1024, "Signed PDF", ct);
                            if (pdfBytes.Length == 0)
                                throw new AgreementConflictException("The signed document could not be generated. Please retry.");
                            _uploads.ValidateGeneratedAgreementPdf(pdfBytes);
                            await using var pdf = new MemoryStream(pdfBytes, writable: false);
                            signedPdfPath = await _store.SaveSignedAsync(tenantId, agreement.Id, pdf, ct);
                            agreement.SignedPdfPath = signedPdfPath;
                        }
                        finally
                        {
                            if (File.Exists(tempPath)) File.Delete(tempPath);
                        }

                        agreement.Status = Agreement.StatusCompleted;
                        agreement.CompletedUtc = DateTime.UtcNow;
                    }
                    else agreement.Status = Agreement.StatusPartiallySigned;

                    agreement.UpdatedUtc = DateTime.UtcNow;
                    await _repo.SaveAsync(tenantId, agreement, ct);
                    signaturePath = null;
                    signedPdfPath = null;
                    return new SignResult(agreement.Status == Agreement.StatusCompleted, total, signedCount);
                }
                catch (ConcurrencyException) when (attempt < 1)
                {
                    if (signedPdfPath is not null)
                    {
                        await _store.DeleteArtifactAsync(tenantId, agreement.Id, signedPdfPath, CancellationToken.None);
                        signedPdfPath = null;
                    }

                    agreement = await _repo.ReloadBySignerTokenAsync(tenantId, request.Token, ct)
                        ?? throw new NotFoundException("Agreement", Guid.Empty);
                    signer = agreement.Signers.FirstOrDefault(s => s.Token == request.Token)
                        ?? throw new NotFoundException("Signer", Guid.Empty);
                    EnsureCanSign(agreement, signer);
                }
            }
        }
        catch
        {
            if (signedPdfPath is not null)
                await _store.DeleteArtifactAsync(tenantId, agreement.Id, signedPdfPath, CancellationToken.None);
            if (signaturePath is not null)
                await _store.DeleteArtifactAsync(tenantId, agreement.Id, signaturePath, CancellationToken.None);
            throw;
        }
    }

    private static void EnsureCanSign(Agreement agreement, Signer signer)
    {
        if (agreement.Status == Agreement.StatusCancelled)
            throw new AgreementConflictException("Agreement cancelled");
        if (!agreement.IsActive)
            throw new AgreementConflictException("Agreement deactivated");
        if (signer.Status == Signer.StatusSigned || signer.SignatureImagePath is not null || signer.SignedAtUtc.HasValue)
            throw new AgreementConflictException("Already signed");
    }
}
