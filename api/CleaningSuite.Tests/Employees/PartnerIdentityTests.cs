using System.Security.Claims;
using System.Text.Json;
using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using Microsoft.AspNetCore.Http;
using Moq;

namespace CleaningSuite.Tests.Employees;

public sealed class PartnerIdentityTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task AccessUsesCurrentCompanyState(bool active, bool enabled, bool allowed)
    {
        var company = new Company { IsActive = active, IsPartnerEnabled = enabled };
        var employee = new Employee { Role = Employee.RolePartner, KeycloakUserId = "owner", PartnerCompanyId = company.Id };
        var people = new Mock<IEmployeeRepository>();
        people.Setup(x => x.GetByKeycloakUserIdAsync("owner", default)).ReturnsAsync(employee);
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(x => x.GetAsync(company.Id, default)).ReturnsAsync(company);
        var context = Context("partner");
        Assert.Equal(allowed, await new EmployeeAccessGuard(people.Object, companies.Object).AllowAsync(context));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("employee")]
    [InlineData("manager")]
    public async Task MixedPartnerAndStaffRolesAreAlwaysDenied(string staffRole)
    {
        var people = new Mock<IEmployeeRepository>();
        var context = Context("partner", staffRole);
        Assert.False(await new EmployeeAccessGuard(people.Object).AllowAsync(context));
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnlinkedPartnerCannotUseTenantOwnerCompatibility()
    {
        Assert.False(await new EmployeeAccessGuard(Mock.Of<IEmployeeRepository>()).AllowAsync(Context("partner")));
    }

    [Fact]
    public async Task MissingBindingAndUnavailableRepositoryFailClosed()
    {
        var employee = new Employee { Role = Employee.RolePartner, KeycloakUserId = "owner" };
        var people = new Mock<IEmployeeRepository>();
        people.Setup(x => x.GetByKeycloakUserIdAsync("owner", default)).ReturnsAsync(employee);
        var guard = new EmployeeAccessGuard(people.Object);
        Assert.False(await guard.AllowAsync(Context("partner")));
        employee.PartnerCompanyId = Guid.NewGuid();
        Assert.False(await guard.AllowAsync(Context("partner")));
    }

    [Fact]
    public async Task CreateChecksCompanyBeforePersistingPartner()
    {
        var company = new Company { IsActive = true, IsPartnerEnabled = false };
        var people = new Mock<IEmployeeRepository>();
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(x => x.GetAsync(company.Id, default)).ReturnsAsync(company);
        var fields = Fields(Employee.RolePartner, company.Id);
        var handler = new CreateEmployeeHandler(people.Object, companies.Object);
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => handler.Handle(new(fields), default));
        people.Verify(x => x.SaveAsync(It.IsAny<Employee>(), default), Times.Never);
        company.IsPartnerEnabled = true;
        await handler.Handle(new(fields), default);
        people.Verify(x => x.SaveAsync(It.Is<Employee>(e => e.Role == "partner" && e.PartnerCompanyId == company.Id), default), Times.Once);
    }

    [Fact]
    public void PartnerBindingIsMandatoryAndForbiddenForStaff()
    {
        var validator = new EmployeeFieldsValidator();
        Assert.False(validator.Validate(Fields("partner", null)).IsValid);
        Assert.False(validator.Validate(Fields("partner", Guid.Empty)).IsValid);
        Assert.True(validator.Validate(Fields("partner", Guid.NewGuid())).IsValid);
        Assert.False(validator.Validate(Fields("employee", Guid.NewGuid())).IsValid);
        Assert.False(Employee.IsWorkRole("partner"));
        Assert.All(new[] { "admin", "manager", "employee" }, role => Assert.True(Employee.IsWorkRole(role)));
    }

    private static EmployeeFields Fields(string role, Guid? company) =>
        new("owner@example.invalid", "Property", "Owner", "", role, null, new(), null, null, null, null, null, company);

    private static DefaultHttpContext Context(params string[] roles)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/partner/me";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim("sub", "owner"), new Claim("email_verified", "true"),
            new Claim("realm_access", JsonSerializer.Serialize(new { roles }))
        }, "test"));
        return context;
    }
}
