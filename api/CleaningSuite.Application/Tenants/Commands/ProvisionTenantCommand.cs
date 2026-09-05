using CleaningSuite.Domain.Tenants;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Application.Tenants.Commands;

public record ProvisionTenantCommand(
    string Slug,
    string CompanyName,
    string OwnerEmail,
    string OwnerFirstName,
    string OwnerLastName) : IRequest<ProvisionTenantResult>;

public record ProvisionTenantResult(
    string Slug,
    string Realm,
    string ClientId,
    string TemporaryPassword,
    bool AlreadyExisted);

public class ProvisionTenantValidator : AbstractValidator<ProvisionTenantCommand>
{
    public ProvisionTenantValidator()
    {
        RuleFor(x => x.Slug).Matches(@"^[a-z0-9][a-z0-9-]{2,62}$");
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OwnerEmail).EmailAddress();
        RuleFor(x => x.OwnerFirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OwnerLastName).NotEmpty().MaximumLength(100);
    }
}

public class ProvisionTenantHandler : IRequestHandler<ProvisionTenantCommand, ProvisionTenantResult>
{
    private readonly IKeycloakProvisioner _keycloak;
    private readonly ITenantRegistry _registry;
    private readonly ITenantProfileRepository _profiles;
    private readonly ILogger<ProvisionTenantHandler> _logger;

    public ProvisionTenantHandler(
        IKeycloakProvisioner keycloak,
        ITenantRegistry registry,
        ITenantProfileRepository profiles,
        ILogger<ProvisionTenantHandler> logger)
    {
        _keycloak = keycloak;
        _registry = registry;
        _profiles = profiles;
        _logger = logger;
    }

    public async Task<ProvisionTenantResult> Handle(ProvisionTenantCommand request, CancellationToken ct)
    {
        var existing = await _registry.GetBySlugAsync(request.Slug, ct);
        if (existing is not null)
        {
            _logger.LogWarning("Tenant {Slug} already registered", request.Slug);
            return new ProvisionTenantResult(
                request.Slug, request.Slug, "cleaning-suite-web", "", AlreadyExisted: true);
        }

        var provisioned = await _keycloak.ProvisionAsync(
            request.Slug,
            request.CompanyName,
            request.OwnerEmail,
            request.OwnerFirstName,
            request.OwnerLastName,
            ["http://localhost:3000/*", "https://readysetsiivous.fi/*"],
            ct);

        var registration = new TenantRegistration
        {
            Slug = request.Slug,
            CompanyName = request.CompanyName,
            KeycloakRealm = request.Slug,
            Status = TenantRegistration.StatusActive,
        };
        await _registry.SaveAsync(registration, ct);

        var profile = new TenantProfile
        {
            Id = CleaningSuite.Domain.Common.TenantDocumentIds.TenantProfile(request.Slug),
            Slug = request.Slug,
            RealmName = request.Slug,
            CompanyName = request.CompanyName,
            Email = request.OwnerEmail,
        };
        await _profiles.SaveAsync(request.Slug, profile, ct);

        _logger.LogInformation("Tenant {Slug} provisioned", request.Slug);

        return new ProvisionTenantResult(
            request.Slug, provisioned.Realm, provisioned.ClientId,
            provisioned.TemporaryPassword, AlreadyExisted: false);
    }
}
