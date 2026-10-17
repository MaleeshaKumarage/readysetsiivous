using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record DeleteAgreementCommand(Guid Id, bool ConfirmSigned = false) : IRequest;

public sealed class DeleteAgreementHandler(ITenantContext tenant, IAgreementRepository agreements)
    : IRequestHandler<DeleteAgreementCommand>
{
    public async Task Handle(DeleteAgreementCommand request, CancellationToken ct)
    {
        var agreement = await agreements.GetAsync(tenant.TenantId, request.Id, ct)
            ?? throw new NotFoundException("Agreement", request.Id);
        if (agreement.IsActive)
            throw new InactiveRecordDeleteConflictException("Deactivate the agreement before deleting it.");
        if (!request.ConfirmSigned && (agreement.Signers.Any(s => s.Status == Signer.StatusSigned
                || s.SignedAtUtc.HasValue || !string.IsNullOrWhiteSpace(s.SignatureImagePath))
            || agreement.SignedPdfPath is not null
            || agreement.Status is Agreement.StatusPartiallySigned or Agreement.StatusCompleted))
            throw new InactiveRecordDeleteConflictException("This agreement contains signed evidence. Explicitly confirm deletion of the signed agreement.");
        await agreements.DeleteAsync(tenant.TenantId, agreement.Id, ct);
    }
}
