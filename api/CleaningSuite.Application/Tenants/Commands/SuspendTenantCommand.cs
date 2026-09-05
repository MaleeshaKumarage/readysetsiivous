using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Tenants;
using MediatR;

namespace CleaningSuite.Application.Tenants.Commands;

public record SetTenantStatusCommand(string Slug, bool Active) : IRequest<Unit>;

public class SetTenantStatusHandler : IRequestHandler<SetTenantStatusCommand, Unit>
{
    private readonly ITenantRegistry _registry;
    private readonly IKeycloakProvisioner _keycloak;
    private readonly ITenantStatusCache _statusCache;

    public SetTenantStatusHandler(
        ITenantRegistry registry,
        IKeycloakProvisioner keycloak,
        ITenantStatusCache statusCache)
    {
        _registry = registry;
        _keycloak = keycloak;
        _statusCache = statusCache;
    }

    public async Task<Unit> Handle(SetTenantStatusCommand request, CancellationToken ct)
    {
        var registration = await _registry.GetBySlugAsync(request.Slug, ct)
            ?? throw new NotFoundException("TenantRegistration", Guid.Empty);

        await _keycloak.SetRealmEnabledAsync(registration.KeycloakRealm, request.Active, ct);

        registration.Status = request.Active
            ? TenantRegistration.StatusActive
            : TenantRegistration.StatusSuspended;
        registration.UpdatedUtc = DateTime.UtcNow;

        await _registry.SaveAsync(registration, ct);
        _statusCache.Evict(request.Slug);
        return Unit.Value;
    }
}

public record ListTenantsQuery : IRequest<IReadOnlyList<TenantRegistration>>;

public class ListTenantsHandler : IRequestHandler<ListTenantsQuery, IReadOnlyList<TenantRegistration>>
{
    private readonly ITenantRegistry _registry;

    public ListTenantsHandler(ITenantRegistry registry) => _registry = registry;

    public Task<IReadOnlyList<TenantRegistration>> Handle(ListTenantsQuery request, CancellationToken ct) =>
        _registry.ListAsync(ct);
}
