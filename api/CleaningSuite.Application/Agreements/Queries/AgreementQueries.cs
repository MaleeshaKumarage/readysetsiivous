using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using MediatR;

namespace CleaningSuite.Application.Agreements.Queries;

public record AgreementListItemDto(Guid Id, string Title, string Status, int SignerCount, int SignedCount, DateTime CreatedUtc);
public record SignerDto(Guid Id, string Name, string Email, string Status, string Token);
public record AgreementDetailDto(Guid Id, string Title, string Status, IReadOnlyList<SignerDto> Signers, DateTime CreatedUtc, DateTime? CompletedUtc)
{
    public static AgreementDetailDto From(Agreement a) => new(
        a.Id, a.Title, a.Status,
        a.Signers.Select(s => new SignerDto(s.Id, s.Name, s.Email, s.Status, s.Token)).ToList(),
        a.CreatedUtc, a.CompletedUtc);
}

public record ListAgreementsQuery : IRequest<IReadOnlyList<AgreementListItemDto>>;

public class ListAgreementsHandler : IRequestHandler<ListAgreementsQuery, IReadOnlyList<AgreementListItemDto>>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public ListAgreementsHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<IReadOnlyList<AgreementListItemDto>> Handle(ListAgreementsQuery request, CancellationToken ct)
    {
        var list = await _repo.ListAsync(_context.TenantId, ct);
        return list.Select(a => new AgreementListItemDto(a.Id, a.Title, a.Status,
            a.Signers.Count, a.Signers.Count(s => s.Status == Signer.StatusSigned), a.CreatedUtc)).ToList();
    }
}

public record GetAgreementQuery(Guid Id) : IRequest<AgreementDetailDto?>;

public class GetAgreementHandler : IRequestHandler<GetAgreementQuery, AgreementDetailDto?>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public GetAgreementHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<AgreementDetailDto?> Handle(GetAgreementQuery request, CancellationToken ct)
    {
        var a = await _repo.GetAsync(_context.TenantId, request.Id, ct);
        return a is null ? null : AgreementDetailDto.From(a);
    }
}

public record PublicAgreementDto(string Title, string Status, int TotalSigners, int SignedCount, bool Completed);

public record GetPublicAgreementQuery(string Token) : IRequest<PublicAgreementDto?>;

public class GetPublicAgreementHandler : IRequestHandler<GetPublicAgreementQuery, PublicAgreementDto?>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public GetPublicAgreementHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<PublicAgreementDto?> Handle(GetPublicAgreementQuery request, CancellationToken ct)
    {
        var a = await _repo.FindBySignerTokenAsync(_context.TenantId, request.Token, ct);
        if (a is null) return null;
        return new PublicAgreementDto(a.Title, a.Status, a.Signers.Count,
            a.Signers.Count(s => s.Status == Signer.StatusSigned),
            a.Status == Agreement.StatusCompleted);
    }
}

public record GetAgreementFileQuery(string Token) : IRequest<string?>;

public class GetAgreementFileHandler : IRequestHandler<GetAgreementFileQuery, string?>
{
    private readonly ITenantContext _context;
    private readonly IAgreementRepository _repo;
    public GetAgreementFileHandler(ITenantContext context, IAgreementRepository repo) { _context = context; _repo = repo; }

    public async Task<string?> Handle(GetAgreementFileQuery request, CancellationToken ct)
    {
        var a = await _repo.FindBySignerTokenAsync(_context.TenantId, request.Token, ct);
        if (a is null) return null;
        return a.Status == Agreement.StatusCompleted
            ? a.SignedPdfPath
            : a.OriginalPdfPath;
    }
}
