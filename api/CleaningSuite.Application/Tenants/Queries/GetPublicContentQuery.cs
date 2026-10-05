using CleaningSuite.Application.Common;
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
    private readonly ITenantCacheService? _cacheService;

    public GetPublicContentHandler(ITenantContext context, ITenantProfileRepository profiles, ITenantCacheService? cacheService = null)
    {
        _context = context;
        _profiles = profiles;
        _cacheService = cacheService;
    }

    public async Task<PublicContentDto?> Handle(GetPublicContentQuery request, CancellationToken ct)
    {
        var fetch = async (CancellationToken cToken) =>
        {
            var profile = await _profiles.GetAsync(_context.TenantId, cToken);
            if (profile is null) return null;

            var pages = profile.Pages.ToDictionary(
                p => p.Key,
                p => p.Value.For(request.Lang) ?? p.Value.For(profile.DefaultLocale) ?? "");

            return new PublicContentDto(profile.Slug, profile.CompanyName, profile.DefaultLocale, pages);
        };

        if (_cacheService is null) return await fetch(ct);

        return await _cacheService.GetOrAddAsync("tenant", $"content_{request.Lang}", fetch, ct: ct);
    }
}
