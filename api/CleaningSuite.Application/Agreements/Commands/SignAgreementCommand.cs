using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record SignAgreementCommand(string Token, string TypedName, byte[] SignaturePng) : IRequest<SignResult>;

public record SignResult(bool Completed, int TotalSigners, int SignedCount);

public class SignAgreementValidator : AbstractValidator<SignAgreementCommand>
{
    public SignAgreementValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.TypedName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SignaturePng).NotEmpty();
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
        var agreement = await _repo.FindBySignerTokenAsync(tenantId, request.Token, ct)
            ?? throw new NotFoundException("Agreement", Guid.Empty);

        var signer = agreement.Signers.FirstOrDefault(s => s.Token == request.Token)
            ?? throw new NotFoundException("Signer", Guid.Empty);

        if (agreement.Status == Agreement.StatusCancelled)
            throw new AgreementConflictException("Agreement cancelled");
        if (signer.Status == Signer.StatusSigned)
            throw new AgreementConflictException("Already signed");

        signer.SignatureImagePath = await _store.SaveSignatureAsync(tenantId, agreement.Id, signer.Id, request.SignaturePng, ct);
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
}
