using CleaningSuite.Application.Tenants;

namespace CleaningSuite.Api.Tenancy;

/// <summary>Reads the tenant id set by TenantResolutionMiddleware from the current request.</summary>
public class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public string TenantId =>
        _accessor.HttpContext?.Items["TenantId"] as string ?? "";
}
