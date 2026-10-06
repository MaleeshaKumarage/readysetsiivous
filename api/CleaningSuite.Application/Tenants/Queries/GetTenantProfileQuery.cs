using CleaningSuite.Domain.Tenants;
using MediatR;

namespace CleaningSuite.Application.Tenants.Queries;

public record GetTenantProfileQuery : IRequest<TenantProfile?>;

public class GetTenantProfileHandler : IRequestHandler<GetTenantProfileQuery, TenantProfile?>
{
    private readonly ITenantContext _context;
    private readonly ITenantProfileRepository _profiles;

    public GetTenantProfileHandler(ITenantContext context, ITenantProfileRepository profiles)
    {
        _context = context;
        _profiles = profiles;
    }

    public Task<TenantProfile?> Handle(GetTenantProfileQuery request, CancellationToken ct) =>
        _profiles.GetAsync(_context.TenantId, ct);
}
