using System;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class OneTimeShiftHandlerTests
{
    private readonly Mock<IShiftRepository> _shifts = new();
    private readonly Mock<ICompanyRepository> _companies = new();
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Mock<ITenantProfileRepository> _profiles = new();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    public OneTimeShiftHandlerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns("readysetsiivous");
        _profiles.Setup(p => p.GetAsync("readysetsiivous", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });
        _companies.Setup(c => c.GetAsync(_companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Company.Create("1234567-8", "Test Company", null, null, null));
        _branches.Setup(b => b.GetAsync(_branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = _branchId, CompanyId = _companyId, Name = "Branch 1", SizeM2 = 34, CleaningType = "Deep" });
        _shifts.Setup(s => s.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private ShiftHandlers.CreateShiftCommandHandler Handler() =>
        new(_shifts.Object, _companies.Object, _branches.Object, null, _profiles.Object, _tenant.Object);

    private static ShiftSchedule OneTime(DateTime date, TimeSpan start, TimeSpan end) => new()
    {
        Type = ShiftScheduleType.OneTime,
        OneTimeDate = date,
        OneTimeStart = start,
        OneTimeEnd = end,
    };

    [Fact]
    public async Task Create_saves_past_one_time_visit_with_validity_on_its_date()
    {
        var date = new DateTime(2026, 10, 1);
        var command = new CreateShiftCommand(_companyId, _branchId, "Past visit",
            OneTime(date, new TimeSpan(9, 0, 0), new TimeSpan(11, 30, 0)), null,
            ValidFrom: new DateTime(2026, 11, 1), ValidUntil: new DateTime(2026, 11, 2));

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.Equal(ShiftScheduleType.OneTime, result.Schedule.Type);
        Assert.Equal(date, result.ValidFrom);
        Assert.Equal(date, result.ValidUntil);
        _shifts.Verify(s => s.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_rejects_visit_time_inside_spring_forward_gap_without_saving()
    {
        // Helsinki clocks jump 03:00 to 04:00 on 2026-03-29, so 03:30 does not exist that day.
        var command = new CreateShiftCommand(_companyId, _branchId, "Gap visit",
            OneTime(new DateTime(2026, 3, 29), new TimeSpan(3, 30, 0), new TimeSpan(5, 0, 0)), null,
            ValidFrom: null, ValidUntil: null);

        var error = await Assert.ThrowsAsync<ValidationException>(() => Handler().Handle(command, CancellationToken.None));

        Assert.Contains("does not exist in the tenant time zone", error.Message);
        _shifts.Verify(s => s.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_rejects_visit_time_inside_spring_forward_gap_without_saving()
    {
        var shift = Shift.Create(_companyId, _branchId, "Visit",
            OneTime(new DateTime(2026, 11, 3), new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0)), null, null, null);
        _shifts.Setup(s => s.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        var handler = new ShiftHandlers.UpdateShiftCommandHandler(_shifts.Object, _companies.Object, _branches.Object,
            null, null, _profiles.Object, _tenant.Object);
        var command = new UpdateShiftCommand(shift.Id, _companyId, _branchId, "Visit",
            OneTime(new DateTime(2026, 3, 29), new TimeSpan(3, 30, 0), new TimeSpan(5, 0, 0)));

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(command, CancellationToken.None));

        _shifts.Verify(s => s.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
