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

    // Size cap guards against base64 blobs and decompression bombs.
    private static bool BeReasonableSize(byte[] png) => png.Length <= MaxSignatureBytes;

    // Verify the 8-byte PNG signature so a non-PNG cannot reach SKBitmap.Decode.
    private static bool BePng(byte[] png) =>
        png.Length >= 8 && png.AsSpan(0, 8).SequenceEqual(PngMagic);

    // Cheap dimension check straight from the PNG IHDR (bytes 16-23), no decode needed.
    // A valid header with an enormous width/height would otherwise decompress into a bomb.
    private static bool BeReasonableDimensions(byte[] png)
    {
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

    public SignAgreementHandler(ITenantContext context, IAgreementRepository repo,
        IAgreementFileStore store, IAgreementDocumentGenerator generator)
    {
        _context = context; _repo = repo; _store = store; _generator = generator;
    }

    public async Task<SignResult> Handle(SignAgreementCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;

        // Load once to validate state and persist the signature image — a durable side
        // effect we deliberately do not repeat on retry.
        var agreement = await _repo.FindBySignerTokenAsync(tenantId, request.Token, ct)
            ?? throw new NotFoundException("Agreement", Guid.Empty);
        var signer = agreement.Signers.FirstOrDefault(s => s.Token == request.Token)
            ?? throw new NotFoundException("Signer", Guid.Empty);

        if (agreement.Status == Agreement.StatusCancelled)
            throw new AgreementConflictException("Agreement cancelled");
        if (signer.Status == Signer.StatusSigned)
            throw new AgreementConflictException("Already signed");

        var signaturePath = await _store.SaveSignatureAsync(tenantId, agreement.Id, signer.Id, request.SignaturePng, ct);

        // Parallel signers race on the agreement's optimistic-concurrency version; retry
        // once against a fresh copy. The signature image is already on disk, so re-applying
        // only re-sets the signer fields and re-evaluates completion. A second conflict
        // surfaces as 409 via the exception mapping.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    agreement = await _repo.FindBySignerTokenAsync(tenantId, request.Token, ct)
                        ?? throw new NotFoundException("Agreement", Guid.Empty);
                    signer = agreement.Signers.FirstOrDefault(s => s.Token == request.Token)
                        ?? throw new NotFoundException("Signer", Guid.Empty);

                    if (agreement.Status == Agreement.StatusCancelled)
                        throw new AgreementConflictException("Agreement cancelled");
                    if (signer.Status == Signer.StatusSigned)
                        throw new AgreementConflictException("Already signed");
                }

                signer.SignatureImagePath = signaturePath;
                signer.TypedName = request.TypedName;
                signer.SignedAtUtc = DateTime.UtcNow;
                signer.Status = Signer.StatusSigned;

                var signedCount = agreement.Signers.Count(s => s.Status == Signer.StatusSigned);
                var total = agreement.Signers.Count;

                if (agreement.AllSigned)
                {
                    // Generate the signed PDF to a temp path, then copy it into the store.
                    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
                    try
                    {
                        await _generator.GenerateSignedPdfAsync(agreement.OriginalPdfPath, agreement.Signers, tempPath, ct);
                        await using var pdf = File.OpenRead(tempPath);
                        var signedPath = await _store.SaveSignedAsync(tenantId, agreement.Id, pdf, ct);
                        agreement.SignedPdfPath = signedPath;
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }

                    agreement.Status = Agreement.StatusCompleted;
                    agreement.CompletedUtc = DateTime.UtcNow;
                }
                else
                {
                    agreement.Status = Agreement.StatusPartiallySigned;
                }

                agreement.UpdatedUtc = DateTime.UtcNow;
                await _repo.SaveAsync(tenantId, agreement, ct);
                return new SignResult(agreement.Status == Agreement.StatusCompleted, total, signedCount);
            }
            catch (ConcurrencyException)
            {
                if (attempt >= 1)
                    throw;
            }
        }
    }
}
