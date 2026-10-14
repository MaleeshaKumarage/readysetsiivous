using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Queries;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Agreements.Commands;

public record SignerInput(string Name, string Email);

public record CreateAgreementCommand(string Title, Guid CompanyId, Stream Pdf, string FileName, IReadOnlyList<SignerInput> Signers)
    : IRequest<AgreementDetailDto>;

public class CreateAgreementValidator : AbstractValidator<CreateAgreementCommand>
{
    public CreateAgreementValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CompanyId).NotEmpty();
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
    private readonly ICompanyRepository _companies;

    public CreateAgreementHandler(ITenantContext context, IAgreementRepository repo, IAgreementFileStore store, ICompanyRepository companies)
    {
        _context = context; _repo = repo; _store = store;
        _companies = companies;
    }

    public async Task<AgreementDetailDto> Handle(CreateAgreementCommand request, CancellationToken ct)
    {
        _ = await _companies.GetAsync(request.CompanyId, ct)
            ?? throw new NotFoundException("Company", request.CompanyId);
        var tenantId = _context.TenantId;
        var agreement = new Agreement
        {
            Id = Guid.NewGuid(),
            Slug = tenantId,
            Title = request.Title,
            CompanyId = request.CompanyId,
            Code = AgreementTokens.NewCode(),
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
