using System.Security.Claims;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Auth;
using Microsoft.Extensions.Caching.Memory;

namespace CleaningSuite.Api.Tenancy;

/// <summary>
/// Resolves the request tenant: realm from JWT issuer for authenticated calls,
/// URL slug for public calls. Rejects tenants that are not registered Active.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _serverUrl;
    private readonly ITenantStatusCache _statusCache;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ITenantStatusCache statusCache,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _serverUrl = configuration["Auth:Keycloak:ServerUrl"]!.TrimEnd('/');
        _statusCache = statusCache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantRegistry registry)
    {
        var path = context.Request.Path.Value ?? "";
        var tenantId = "";

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var issuer = context.User.FindFirstValue("iss") ?? "";
            var realm = KeycloakJwt.RealmFromIssuer(issuer, _serverUrl);
            context.Items["Realm"] = realm;

            if (path.StartsWith("/api/v1/system", StringComparison.OrdinalIgnoreCase))
            {
                // System endpoints act on the registry partition; auth is checked by the controller.
                tenantId = Infrastructure.Persistence.TenantIds.Registry;
            }
            else
            {
                tenantId = realm;
                if (RequiresActiveTenant(path)
                    && !await IsActiveAsync(registry, realm, context.RequestAborted))
                {
                    await WriteInactiveAsync(context, realm);
                    return;
                }
            }
        }
        else if (TryGetPublicSlug(path, out var slug))
        {
            context.Items["Slug"] = slug;
            tenantId = slug;
            if (!await IsActiveAsync(registry, slug, context.RequestAborted))
            {
                await WriteInactiveAsync(context, slug);
                return;
            }
        }

        context.Items["TenantId"] = tenantId;
        await _next(context);
    }

    /// <summary>Admin and public endpoints must resolve to a registered Active tenant.</summary>
    private static bool RequiresActiveTenant(string path) =>
        path.Contains("/admin/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/public/", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetPublicSlug(string path, out string slug)
    {
        slug = "";
        const string prefix = "/api/v1/public/";
        var index = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        var rest = path[(index + prefix.Length)..];
        var slash = rest.IndexOf('/');
        slug = slash >= 0 ? rest[..slash] : rest;
        return slug.Length > 0;
    }

    private async Task<bool> IsActiveAsync(ITenantRegistry registry, string tenantId, CancellationToken ct)
    {
        var registration = _statusCache.Get(tenantId);
        if (registration is null)
        {
            registration = await registry.GetBySlugAsync(tenantId, ct);
            if (registration is not null)
                _statusCache.Set(tenantId, registration);
        }

        return registration?.Status == TenantRegistration.StatusActive;
    }

    private async Task WriteInactiveAsync(HttpContext context, string tenantId)
    {
        _logger.LogWarning("Rejected request for unknown/inactive tenant {Tenant}", tenantId);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://cleaning-suite.local/errors/tenant-inactive",
            title = "Tenant is not active",
            status = 403,
        });
    }
}
