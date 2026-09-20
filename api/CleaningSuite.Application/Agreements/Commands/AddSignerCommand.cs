using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record AddSignerCommand(Guid AgreementId, string Name, string Email) : IRequest<Unit>;

public class AddSignerValidator : AbstractValidator<AddSignerCommand>
{
    public AddSignerValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Email).EmailAddress();
    }
}

public class AddSignerHandler : IRequestHandler<AddSignerCommand, Unit>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public AddSignerHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<Unit> Handle(AddSignerCommand request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.AgreementId, ct)
            ?? throw new NotFoundException("Agreement", request.AgreementId);
        if (a.Status != Agreement.StatusDraft && a.Status != Agreement.StatusPartiallySigned)
            throw new InvalidOperationException("Agreement not editable");

        a.Signers.Add(new Signer { Name = request.Name, Email = request.Email, Token = AgreementTokens.New() });
        a.UpdatedUtc = DateTime.UtcNow;
        await _repo.SaveAsync(_context.TenantId, a, ct);
        return Unit.Value;
    }
}
