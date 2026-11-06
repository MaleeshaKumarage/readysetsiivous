using CleaningSuite.Domain.Employees;
using Microsoft.AspNetCore.Http;

namespace CleaningSuite.Api.Auth;

/// <summary>The single rule for which request may act as a staff member: an active, linked, work-role employee.</summary>
public static class StaffActor
{
    public static Employee RequireActive(HttpContext http)
    {
        var employee = http.Items["Employee"] as Employee;
        if (employee is null || !employee.IsActive || employee.IdentitySyncPending || !Employee.IsWorkRole(employee.Role)
            || string.IsNullOrWhiteSpace(employee.KeycloakUserId))
            throw new UnauthorizedAccessException("An active linked staff account is required.");
        return employee;
    }
}
