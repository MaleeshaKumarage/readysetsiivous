using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record SetAgreementActiveCommand(Guid AgreementId, bool Active) : IRequest<Unit>;

public class SetAgreementActiveHandler : IRequestHandler<SetAgreementActiveCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public SetAgreementActiveHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<Unit> Handle(SetAgreementActiveCommand request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.AgreementId, ct)
            ?? throw new NotFoundException("Agreement", request.AgreementId);

        if (request.Active && a.Status == Agreement.StatusCancelled)
            throw new AgreementConflictException("Cancelled agreements cannot be reactivated. The original agreement and signing evidence are preserved.");
        a.IsActive = request.Active;
        a.UpdatedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(_context.TenantId, a, ct);
        return Unit.Value;
    }
}
