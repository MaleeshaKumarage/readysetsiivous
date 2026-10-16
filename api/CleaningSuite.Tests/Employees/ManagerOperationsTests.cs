using System.Text.Json;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using MediatR;
using Moq;

namespace CleaningSuite.Tests.Employees;
public class ManagerOperationsTests
{
    [Fact]
    public async Task EditPreservesVersionTemplateNotesParentsAndHistoricalPlan()
    {
        var f = new Fixture(); f.Shift.QualityCycleTemplateId = Guid.NewGuid(); f.Shift.Notes = "admin-private";
        f.Shift.RecordQualityCyclePlan(DateTime.UtcNow.AddDays(-1)); var version = f.Shift.Version;
        var result = await f.Service.Update(f.Shift.Id, "Updated", new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(9), DailyEnd = TimeSpan.FromHours(11) }, true, null, null, default);
        Assert.Equal("admin-private", f.Shift.Notes); Assert.Equal(version, f.Shift.Version);
        Assert.Equal(f.Shift.QualityCycleTemplateId, f.Shift.QualityCyclePlans[0].TemplateId);
        Assert.Equal(f.Shift.QualityCycleTemplateId, f.Shift.QualityCyclePlans[1].TemplateId);
        f.Shifts.Verify(r => r.SaveAsync(f.Shift, It.IsAny<CancellationToken>()), Times.Once);
        var body = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("admin-private", body); Assert.DoesNotContain("qualityCycle", body); Assert.DoesNotContain("version", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(new[] { "id", "companyId", "branchId", "name", "schedule", "isActive", "validFrom", "validUntil", "timeZoneId" }.Order(), json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.IsType<ManagerScheduleResponse>(result.Schedule);
    }

    [Theory]
    [InlineData("missing-company")] [InlineData("inactive-company")] [InlineData("missing-branch")]
    [InlineData("inactive-branch")] [InlineData("foreign-parent")] [InlineData("missing-shift")]
    public async Task InvalidParentOrForeignResourceRejectsWithoutWriting(string scenario)
    {
        var f = new Fixture();
        if (scenario == "missing-company") f.Companies.Setup(r => r.GetAsync(f.Company.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Company?)null);
        if (scenario == "inactive-company") f.Company.IsActive = false;
        if (scenario == "missing-branch") f.Branches.Setup(r => r.GetAsync(f.Branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Branch?)null);
        if (scenario == "inactive-branch") f.Branch.IsActive = false;
        if (scenario == "foreign-parent") f.Branch.CompanyId = Guid.NewGuid();
        if (scenario == "missing-shift") f.Shifts.Setup(r => r.GetAsync(f.Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Shift?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.Update(f.Shift.Id, "Name", f.Shift.Schedule, true, null, null, default));
        f.Shifts.Verify(r => r.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(f.Mediator.Invocations);
    }

    [Fact]
    public async Task InvalidScheduleAndValidityDoNotMutateOrSave()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.Update(f.Shift.Id, "Changed", new(), true, null, null, default));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.Update(f.Shift.Id, "Changed", f.Shift.Schedule, true, DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), default));
        Assert.Equal("Original", f.Shift.Name); f.Shifts.Verify(r => r.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public Company Company = Company.Create("TEST", "Company"); public Branch Branch;
        public Shift Shift; public Mock<IShiftRepository> Shifts = new(); public Mock<ICompanyRepository> Companies = new();
        public Mock<IBranchRepository> Branches = new(); public Mock<IMediator> Mediator = new(); public ManagerOperations Service;
        public Fixture() {
            Branch = Branch.Create(Company.Id, "Branch"); Shift = Shift.Create(Company.Id, Branch.Id, "Original", new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, null, null, null); Shift.Version = Guid.NewGuid();
            Shifts.Setup(r => r.GetAsync(Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Shift);
            Companies.Setup(r => r.GetAsync(Company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Company);
            Branches.Setup(r => r.GetAsync(Branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Branch);
            Service = new(Shifts.Object, Mock.Of<IEmployeeRepository>(), Companies.Object, Branches.Object, Mock.Of<IQualityCycleDispatcher>(), Mediator.Object);
        }
    }
}
