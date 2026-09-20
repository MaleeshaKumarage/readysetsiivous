using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Queries;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record SignerInput(string Name, string Email);

public record CreateAgreementCommand(string Title, Stream Pdf, string FileName, IReadOnlyList<SignerInput> Signers)
    : IRequest<AgreementDetailDto>;

public class CreateAgreementValidator : AbstractValidator<CreateAgreementCommand>
{
    public CreateAgreementValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Signers).NotEmpty();
        RuleForEach(x => x.Signers).ChildRules(s =>
        {
            s.RuleFor(x => x.Name).NotEmpty();
            s.RuleFor(x => x.Email).EmailAddress();
        });
    }
}

public class CreateAgreementHandler : IRequestHandler<CreateAgreementCommand, AgreementDetailDto>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    private readonly IAgreementFileStore _store;

    public CreateAgreementHandler(ITenantContext context, IAgreementRepository repo, IAgreementFileStore store)
    {
        _context = context; _repo = repo; _store = store;
    }

    public async Task<AgreementDetailDto> Handle(CreateAgreementCommand request, CancellationToken ct)
    {
        var tenantId = _context.TenantId;
        var agreement = new Agreement
        {
            Id = Guid.NewGuid(),
            Slug = tenantId,
            Title = request.Title,
            Signers = request.Signers.Select(s => new Signer
            {
                Name = s.Name, Email = s.Email, Token = AgreementTokens.New(),
            }).ToList(),
        };

        agreement.OriginalPdfPath = await _store.SaveOriginalAsync(tenantId, agreement.Id, request.Pdf, ct);
        await _repo.SaveAsync(tenantId, agreement, ct);
        return AgreementDetailDto.From(agreement);
    }
}
