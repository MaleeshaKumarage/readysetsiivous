using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record CancelAgreementCommand(Guid AgreementId) : IRequest<Unit>;

public class CancelAgreementHandler : IRequestHandler<CancelAgreementCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public CancelAgreementHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<Unit> Handle(CancelAgreementCommand request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.AgreementId, ct)
            ?? throw new NotFoundException("Agreement", request.AgreementId);
        if (a.Status == Agreement.StatusCompleted)
            throw new InvalidOperationException("Completed agreement cannot be cancelled");

        a.Status = Agreement.StatusCancelled;
        a.UpdatedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(_context.TenantId, a, ct);
        return Unit.Value;
    }
}
