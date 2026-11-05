using System.Collections.Generic;
using CleaningSuite.Domain.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using FluentValidation.TestHelper;
using Xunit;

namespace CleaningSuite.Tests.Employees;

public class EmployeeFieldMessageTests
{
    private static EmployeeFields Fields(string role = "employee", decimal? payRate = null) => new(
        Email: "cleaner@example.test", FirstName: "Ann", LastName: "Cleaner", Phone: "+358401234567",
        Role: role, ColorHex: null, DefaultHours: new Dictionary<string, WorkHours>(), Skills: null,
        ServiceAreas: null, PayRate: payRate, Certifications: null, Notes: null);

    [Fact]
    public void Unknown_role_tells_the_user_the_allowed_roles()
    {
        var result = new EmployeeFieldsValidator().TestValidate(Fields(role: "superuser"));
        result.ShouldHaveValidationErrorFor("Role")
            .WithErrorMessage("Choose a role: admin, manager, employee or partner. Then save again.");
    }

    [Fact]
    public void Negative_pay_rate_says_to_enter_zero_or_more()
    {
        var result = new EmployeeFieldsValidator().TestValidate(Fields(payRate: -5m));
        result.ShouldHaveValidationErrorFor("PayRate")
            .WithErrorMessage("Pay rate cannot be negative. Enter 0 or a positive amount.");
    }
}
