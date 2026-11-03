using System.Text.Json;
using CleaningSuite.Api.Controllers;
using CleaningSuite.Domain.Employees;
namespace CleaningSuite.Tests.Employees;

public class EmployeeWorkPreferenceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public void New_and_historical_employee_documents_default_to_manual()
    {
        Assert.False(new Employee().AutoAcceptWork);
        Assert.False(JsonSerializer.Deserialize<Employee>("{}", Json)!.AutoAcceptWork);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"autoAcceptWork\":null}")]
    [InlineData("{\"autoAcceptWork\":\"true\"}")]
    [InlineData("{\"autoAcceptWork\":true,\"employeeId\":\"foreign\"}")]
    [InlineData("{\"autoAcceptWork\":true,\"payRate\":10}")]
    public void Preference_contract_requires_only_a_boolean(string payload) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkPreferenceRequest>(payload, Json));
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Preference_contract_accepts_both_choices(bool enabled) =>
        Assert.Equal(enabled, JsonSerializer.Deserialize<WorkPreferenceRequest>("{\"autoAcceptWork\":" + enabled.ToString().ToLowerInvariant() + "}", Json)!.AutoAcceptWork);
    [Theory]
    [InlineData(null, true, false, "linked")]
    [InlineData("partner", true, false, "linked")]
    [InlineData("employee", false, false, "linked")]
    [InlineData("employee", true, true, "linked")]
    [InlineData("employee", true, false, "")]
    public async Task Self_endpoint_rejects_unbound_partner_inactive_pending_and_unlinked_actors(string? role, bool active, bool pending, string subject)
    {
        var controller = new MeController(null!, null!, null!);
        controller.ControllerContext = new() { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        if (role is not null) controller.HttpContext.Items["Employee"] = new Employee
            { Role = role, IsActive = active, IdentitySyncPending = pending, KeycloakUserId = subject };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.WorkPreferences(null!, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.SetWorkPreferences(new() { AutoAcceptWork = true }, null!, default));
    }

    [Fact]
    public void Generic_employee_fields_have_no_consent_mutation_property() =>
        Assert.DoesNotContain(typeof(CleaningSuite.Application.Employees.Commands.EmployeeFields).GetProperties(), p => p.Name == nameof(Employee.AutoAcceptWork));
}
