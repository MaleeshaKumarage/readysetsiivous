using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Employees;

public class EmployeeInviteTests
{
    [Fact]
    public async Task Invite_CreatesKeycloakUser_StoresId_AndSendsEmail()
    {
        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            Email = "e@x.fi",
            FirstName = "A",
            LastName = "B",
            Role = Employee.RoleEmployee,
        };

        var empRepo = new Mock<IEmployeeRepository>();
        empRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);

        var ctx = new Mock<ITenantContext>();
        ctx.Setup(c => c.TenantId).Returns("readysetsiivous");

        var provisioner = new Mock<IKeycloakProvisioner>();
        provisioner
            .Setup(p => p.InviteEmployeeAsync("readysetsiivous", "e@x.fi", "A", "B", Employee.RoleEmployee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitedEmployee("kc-user-1", "TempPwd123"));

        var email = new Mock<IEmailSender>();
        var config = new Mock<IConfiguration>();
        config.SetupGet(c => c["App:FrontendBaseUrl"]).Returns("https://readysetsiivous.fi");

        var handler = new InviteEmployeeHandler(empRepo.Object, ctx.Object, provisioner.Object, email.Object, config.Object);

        var result = await handler.Handle(new InviteEmployeeCommand(emp.Id), CancellationToken.None);

        Assert.Equal("e@x.fi", result.Email);
        Assert.Equal("TempPwd123", result.TemporaryPassword);
        Assert.Equal("kc-user-1", emp.KeycloakUserId);
        Assert.NotNull(emp.InvitedAtUtc);
        empRepo.Verify(r => r.SaveAsync(emp, It.IsAny<CancellationToken>()), Times.Once);
        email.Verify(e => e.SendAsync("e@x.fi", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
