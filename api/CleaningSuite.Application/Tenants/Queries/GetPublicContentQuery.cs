using CleaningSuite.Domain.Tenants;
using MediatR;

namespace CleaningSuite.Application.Tenants.Queries;

public record GetPublicContentQuery(string Lang) : IRequest<PublicContentDto?>;

public record PublicContentDto(
    string Slug,
    string CompanyName,
    string DefaultLocale,
    Dictionary<string, string> Pages);

public class GetPublicContentHandler : IRequestHandler<GetPublicContentQuery, PublicContentDto?>
{
    private readonly ITenantContext _context;
    private readonly ITenantProfileRepository _profiles;

    public GetPublicContentHandler(ITenantContext context, ITenantProfileRepository profiles)
    {
        _context = context;
        _profiles = profiles;
    }

    public async Task<PublicContentDto?> Handle(GetPublicContentQuery request, CancellationToken ct)
    {
        var profile = await _profiles.GetAsync(_context.TenantId, ct);
        if (profile is null)
            return null;

        var pages = profile.Pages.ToDictionary(
            p => p.Key,
            p => p.Value.For(request.Lang) ?? p.Value.For(profile.DefaultLocale) ?? "");

        return new PublicContentDto(profile.Slug, profile.CompanyName, profile.DefaultLocale, pages);
    }
}
