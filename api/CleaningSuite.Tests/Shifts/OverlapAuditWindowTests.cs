using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class OverlapAuditWindowTests
{
    // The client sends calendar dates as UTC midnights. For a tenant west of UTC, converting that midnight
    // would move the window back to the previous local day. The audit must query the sent calendar day.
    [Fact]
    public async Task Audit_window_starts_on_the_sent_calendar_day_for_a_tenant_west_of_utc()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Evening clean", new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Sunday], WeeklyStart = TimeSpan.FromHours(18), WeeklyEnd = TimeSpan.FromHours(20) }, null, new DateTime(2026, 10, 1), null);
        var repo = new Mock<IShiftRepository>();
        repo.Setup(r => r.ListForReportingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shift> { shift });
        repo.Setup(r => r.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftOccurrenceException>());
        repo.Setup(r => r.ListAssignmentHistoryAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ShiftAssignment>());
        repo.Setup(r => r.ListOccurrenceAssignmentsAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ShiftOccurrenceAssignment>());
        var profiles = new Mock<ITenantProfileRepository>();
        profiles.Setup(p => p.GetAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = "America/New_York" });
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns("tenant-a");

        var handler = new ShiftOverlapAuditQueryHandler(repo.Object, profiles.Object, tenant.Object);
        await handler.Handle(new ShiftOverlapAuditQuery(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

        var expectedFrom = TenantTimeZone.StartOfLocalDay(new DateTime(2026, 11, 1), zone);
        repo.Verify(r => r.ListOccurrenceExceptionsAsync(shift.Id, expectedFrom, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
