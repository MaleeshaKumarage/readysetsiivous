using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record RemoveSignerCommand(Guid AgreementId, Guid SignerId) : IRequest<Unit>;

public class RemoveSignerHandler : IRequestHandler<RemoveSignerCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public RemoveSignerHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<Unit> Handle(RemoveSignerCommand request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.AgreementId, ct)
            ?? throw new NotFoundException("Agreement", request.AgreementId);
        if (a.Status == Agreement.StatusCompleted || a.Status == Agreement.StatusCancelled)
            throw new AgreementConflictException("This agreement is closed, so its signers cannot be changed. Keep it for the record.");

        var signer = a.Signers.FirstOrDefault(s => s.Id == request.SignerId);
        if (signer is null) return Unit.Value; // Already removed: repeating the request is harmless.

        // Signed evidence stays on the record; a signer who signed is never removed.
        if (Agreement.HasSigned(signer))
            throw new AgreementConflictException("This signer has already signed, so they cannot be removed. Keep the agreement for the record.");

        var remaining = a.Signers.Where(s => s.Id != request.SignerId).ToList();
        if (remaining.Count == 0)
            throw new AgreementConflictException("An agreement needs at least one signer. Add another signer before removing this one.");
        if (remaining.All(Agreement.HasSigned))
            throw new AgreementConflictException("Every other signer has already signed, so removing this signer would leave the agreement unable to finish. Keep this signer, or cancel the agreement instead.");

        a.Signers.Remove(signer);
        a.UpdatedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(_context.TenantId, a, ct);
        return Unit.Value;
    }
}
