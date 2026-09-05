using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Employees;

/// <summary>Staff member. KeycloakUserId links the login to this document.</summary>
public class Employee : BaseDocument
{
    public const string RoleAdmin = "admin";
    public const string RoleEmployee = "employee";

    public string KeycloakUserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Role { get; set; } = RoleEmployee;
    public bool IsActive { get; set; } = true;
    public string? ColorHex { get; set; }

    /// <summary>Default weekly working hours. Keys: Monday..Sunday. Missing or null End means closed.</summary>
    public Dictionary<string, WorkHours> DefaultHours { get; set; } = new();
}
