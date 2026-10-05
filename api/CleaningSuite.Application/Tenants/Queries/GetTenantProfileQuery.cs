using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Tenants;
using MediatR;

namespace CleaningSuite.Application.Tenants.Queries;

public record GetTenantProfileQuery : IRequest<TenantProfile?>;

public class GetTenantProfileHandler : IRequestHandler<GetTenantProfileQuery, TenantProfile?>
{
    private readonly ITenantContext _context;
    private readonly ITenantProfileRepository _profiles;
    private readonly ITenantCacheService? _cacheService;

    public GetTenantProfileHandler(ITenantContext context, ITenantProfileRepository profiles, ITenantCacheService? cacheService = null)
    {
        _context = context;
        _profiles = profiles;
        _cacheService = cacheService;
    }

    public async Task<TenantProfile?> Handle(GetTenantProfileQuery request, CancellationToken ct)
    {
        var fetch = (CancellationToken cToken) => _profiles.GetAsync(_context.TenantId, cToken);
        if (_cacheService is null) return await fetch(ct);
        return await _cacheService.GetOrAddAsync("tenant", "profile", fetch, ct: ct);
    }
}
