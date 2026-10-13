using System.Security.Claims;

namespace CleaningSuite.Api.Auth;

/// <summary>Audit platform outcomes, including rejection before controller execution.</summary>
public sealed class PlatformAuditMiddleware(RequestDelegate next, ILogger<PlatformAuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/system/tenants", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        var failed = false;
        try { await next(context); }
        catch { failed = true; throw; }
        finally
        {
            var authenticated = context.User.Identity?.IsAuthenticated == true;
            var status = failed ? 500 : context.Response.StatusCode;
            logger.LogInformation("Platform tenant operation {Method} {Operation} actor {Actor} issuer {Issuer} tenant {Tenant} status {Status} outcome {Outcome}",
                context.Request.Method, context.Request.RouteValues["action"] ?? "unknown",
                authenticated ? context.User.FindFirstValue("sub") ?? "unknown" : "unauthenticated",
                authenticated ? context.User.FindFirstValue("iss") ?? "unknown" : "unvalidated",
                context.Items["PlatformTargetTenant"] ?? context.Request.RouteValues["slug"] ?? "registry", status,
                status is 401 or 403 ? "rejected" : status == 409 ? "conflict" : status >= 400 ? "failed" : "succeeded");
        }
    }
}
