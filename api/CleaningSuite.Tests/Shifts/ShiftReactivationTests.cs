using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;

namespace CleaningSuite.Tests.Shifts;
public class ShiftReactivationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReactivationPreservesAllFieldsAndHistoryAndIsIdempotent(bool expired)
    {
        var company = Company.Create("TEST", "Company"); var branch = Branch.Create(company.Id, "Branch");
        var template = Guid.NewGuid();
        var shift = Shift.Create(company.Id, branch.Id, "Shift", new() { Type = ShiftScheduleType.DailySameTime,
            DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, "admin-note", DateTime.UtcNow.AddDays(-5), expired ? DateTime.UtcNow.AddDays(-2) : null, template);
        shift.RecordQualityCyclePlan(DateTime.UtcNow.AddDays(-5)); shift.Deactivate(); shift.RecordQualityCyclePlan(DateTime.UtcNow.AddDays(-1));
        var history = shift.QualityCyclePlans.Take(2).ToArray(); var until = shift.ValidUntil;
        var shifts = new Mock<IShiftRepository>(); var companies = new Mock<ICompanyRepository>(); var branches = new Mock<IBranchRepository>(); var dispatcher = new Mock<IQualityCycleDispatcher>();
        shifts.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        companies.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        branches.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        var handler = new ShiftHandlers.ReactivateShiftCommandHandler(shifts.Object, companies.Object, branches.Object, dispatcher.Object);
        var result = await handler.Handle(new(shift.Id), default); await handler.Handle(new(shift.Id), default);
        Assert.True(result.IsActive); Assert.Equal(template, result.QualityCycleTemplateId); Assert.Equal("admin-note", result.Notes);
        Assert.Equal(until, result.ValidUntil); Assert.Equal(company.Id, result.CompanyId); Assert.Equal(branch.Id, result.BranchId);
        Assert.Equal(3, shift.QualityCyclePlans.Count); Assert.Same(history[0], shift.QualityCyclePlans[0]); Assert.Same(history[1], shift.QualityCyclePlans[1]);
        Assert.False(shift.QualityCyclePlans[1].IsActive); Assert.True(shift.QualityCyclePlans[2].IsActive);
        if (expired) Assert.Empty(QualityCycleOccurrences.InWindow(shift, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7)));
        shifts.Verify(r => r.SaveAsync(shift, It.IsAny<CancellationToken>()), Times.Once);
        dispatcher.Verify(r => r.EnsureUpcomingAsync(shift.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InactiveParentRejectsReactivationWithoutWriting()
    {
        var company = Company.Create("TEST", "Company"); var branch = Branch.Create(company.Id, "Branch"); company.IsActive = false;
        var shift = new Shift { CompanyId = company.Id, BranchId = branch.Id, IsActive = false };
        var shifts = new Mock<IShiftRepository>(); var companies = new Mock<ICompanyRepository>(); var branches = new Mock<IBranchRepository>(); var dispatcher = new Mock<IQualityCycleDispatcher>();
        shifts.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        companies.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        branches.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        await Assert.ThrowsAsync<ShiftConflictException>(() => new ShiftHandlers.ReactivateShiftCommandHandler(shifts.Object, companies.Object, branches.Object, dispatcher.Object).Handle(new(shift.Id), default));
        Assert.False(shift.IsActive); shifts.Verify(r => r.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Never); Assert.Empty(dispatcher.Invocations);
    }
}
