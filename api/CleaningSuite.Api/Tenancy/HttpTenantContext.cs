using CleaningSuite.Application.Tenants;

namespace CleaningSuite.Api.Tenancy;

/// <summary>Reads the tenant id set by TenantResolutionMiddleware from the current request.</summary>
public class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;
    private string _backgroundTenantId = "";

    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public string TenantId =>
        _accessor.HttpContext?.Items["TenantId"] as string ?? _backgroundTenantId;

    public void SetBackgroundTenant(string tenantId)
    {
        if (_accessor.HttpContext is not null) throw new InvalidOperationException("Cannot override an HTTP request tenant.");
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant is required.");
        _backgroundTenantId = tenantId;
    }
}
