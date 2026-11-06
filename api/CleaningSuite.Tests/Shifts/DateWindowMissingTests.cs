using System;
using System.Threading.Tasks;
using CleaningSuite.Application.Shifts;
using System.Linq;
using FluentValidation;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class DateWindowMissingTests
{
    [Fact]
    public async Task A_missing_audit_end_date_asks_for_the_end_date()
    {
        var handler = new ShiftOverlapAuditQueryHandler(new Mock<CleaningSuite.Application.Shifts.IShiftRepository>().Object,
            new Mock<CleaningSuite.Application.Tenants.ITenantProfileRepository>().Object, new Mock<CleaningSuite.Application.Tenants.ITenantContext>().Object);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ShiftOverlapAuditQuery(new DateTime(2026, 12, 1), default), default));
        Assert.Contains("end date", ex.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task A_missing_schedule_start_date_asks_for_the_start_date()
    {
        var handler = new EmployeeScheduleQueryHandler(new Mock<CleaningSuite.Application.Shifts.IShiftRepository>().Object,
            new Mock<CleaningSuite.Application.Tenants.ITenantProfileRepository>().Object, new Mock<CleaningSuite.Application.Tenants.ITenantContext>().Object);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new EmployeeScheduleQuery(default, new DateTime(2026, 12, 1)), default));
        Assert.Contains("start date", ex.Errors.Single().ErrorMessage);
    }
}
