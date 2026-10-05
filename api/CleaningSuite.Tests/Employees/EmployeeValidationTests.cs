using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Domain.Employees;
using FluentValidation.TestHelper;
using Xunit;

namespace CleaningSuite.Tests.Employees;

public class EmployeeValidationTests
{
    private readonly CreateEmployeeValidator _validator = new();

    [Fact]
    public void CreateEmployeeValidator_Succeeds_WithValidEmployeeFields()
    {
        var fields = new EmployeeFields(
            Email: "cleaner@example.com",
            FirstName: "Matti",
            LastName: "Meikäläinen",
            Phone: "+358401234567",
            Role: Employee.RoleEmployee,
            ColorHex: null,
            DefaultHours: new(),
            Skills: new(),
            ServiceAreas: new(),
            PayRate: null,
            Certifications: new(),
            Notes: null
        );

        var command = new CreateEmployeeCommand(fields);
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateEmployeeValidator_Fails_WhenEmailIsInvalid()
    {
        var fields = new EmployeeFields(
            Email: "invalid-email",
            FirstName: "Matti",
            LastName: "Meikäläinen",
            Phone: "+358401234567",
            Role: Employee.RoleEmployee,
            ColorHex: null,
            DefaultHours: new(),
            Skills: null,
            ServiceAreas: null,
            PayRate: null,
            Certifications: null,
            Notes: null
        );

        var command = new CreateEmployeeCommand(fields);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Fields.Email");
    }
}
