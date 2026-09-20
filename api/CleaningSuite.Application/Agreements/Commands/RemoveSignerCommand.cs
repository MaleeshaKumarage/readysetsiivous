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
            throw new InvalidOperationException("Agreement not editable");

        a.Signers.RemoveAll(s => s.Id == request.SignerId);
        a.UpdatedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(_context.TenantId, a, ct);
        return Unit.Value;
    }
}
