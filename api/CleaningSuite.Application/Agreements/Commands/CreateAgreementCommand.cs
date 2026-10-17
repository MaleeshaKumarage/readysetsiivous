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
    private readonly IUploadContentValidator _uploads;

    public CreateAgreementHandler(ITenantContext context, IAgreementRepository repo, IAgreementFileStore store, ICompanyRepository companies, IUploadContentValidator uploads)
    {
        _context = context; _repo = repo; _store = store;
        _companies = companies;
        _uploads = uploads;
    }

    public async Task<AgreementDetailDto> Handle(CreateAgreementCommand request, CancellationToken ct)
    {
        _ = await _companies.GetAsync(request.CompanyId, ct)
            ?? throw new NotFoundException("Company", request.CompanyId);
        var pdfBytes = await BoundedStreamReader.ReadAsync(request.Pdf, 20 * 1024 * 1024, "Agreement PDF", ct);
        _uploads.ValidateAgreementPdf(pdfBytes);
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

        await using var validatedPdf = new MemoryStream(pdfBytes, writable: false);
        agreement.OriginalPdfPath = await _store.SaveOriginalAsync(tenantId, agreement.Id, validatedPdf, ct);
        try { await _repo.SaveAsync(tenantId, agreement, ct); }
        catch
        {
            await _store.DeleteArtifactAsync(tenantId, agreement.Id, agreement.OriginalPdfPath, ct);
            throw;
        }
        return AgreementDetailDto.From(agreement);
    }
}
