using System.Security.Claims;
using System.Text.Json;
using CleaningSuite.Application.Employees;
using CleaningSuite.Domain.Employees;

namespace CleaningSuite.Api.Auth;
/// <summary>Database access policy for linked employees; old JWTs cannot bypass lifecycle changes.</summary>
public sealed class EmployeeAccessGuard(IEmployeeRepository employees)
{
    public async Task<bool> AllowAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/v1/admin/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/v1/manager", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/v1/me", StringComparison.OrdinalIgnoreCase)) return true;
        if (context.User.Identity?.IsAuthenticated != true) return true;
        var sub = context.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub)) return await DenyAsync(context, "The login identity has no subject.");
        var employee = await employees.GetByKeycloakUserIdAsync(sub, context.RequestAborted);
        var roles = RealmRoles(context.User);
        if (employee is null) {
            // Provisioned tenant owners predate employee documents. Employee logins must be linked.
            return (roles.Contains(Employee.RoleEmployee) || roles.Contains(Employee.RoleManager)) && !roles.Contains(Employee.RoleAdmin)
                ? await DenyAsync(context, "This login is not linked to an employee. Contact your tenant administrator.") : true;
        }
        if (employee.IdentitySyncPending) return await DenyAsync(context, "Employee login synchronization is pending. Contact your tenant administrator.");
        if (!employee.IsActive) return await DenyAsync(context, "This employee account is inactive.");
        if (!string.Equals(context.User.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase))
            return await DenyAsync(context, "Complete email verification before using this account.");
        var matches = employee.Role == Employee.RoleAdmin ? roles.Contains(Employee.RoleAdmin)
            : employee.Role == Employee.RoleManager ? roles.Contains(Employee.RoleManager) && !roles.Contains(Employee.RoleAdmin) && !roles.Contains(Employee.RoleEmployee)
            : employee.Role == Employee.RoleEmployee && roles.Contains(Employee.RoleEmployee) && !roles.Contains(Employee.RoleAdmin) && !roles.Contains(Employee.RoleManager);
        if (!matches) return await DenyAsync(context, "Account permissions changed. Sign in again or contact your tenant administrator.");
        context.Items["Employee"] = employee;
        return true;
    }
    private static HashSet<string> RealmRoles(ClaimsPrincipal user) {
        try { using var document = JsonDocument.Parse(user.FindFirstValue("realm_access") ?? "{}");
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
                ? roles.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal) : new();
        } catch (JsonException) { return new(); }
    }
    private static async Task<bool> DenyAsync(HttpContext context, string detail) {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { title = "Employee access denied", status = 403, detail }); return false;
    }
}
