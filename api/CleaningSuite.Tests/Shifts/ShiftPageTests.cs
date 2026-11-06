using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class ShiftPageTests
{
    private static readonly Guid CompanyA = Guid.NewGuid();

    private static (ShiftHandlers.ListShiftPageQueryHandler handler, Shift done) Build()
    {
        var weekly = Shift.Create(CompanyA, Guid.NewGuid(), "Weekly clean", new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Monday], WeeklyStart = TimeSpan.FromHours(6), WeeklyEnd = TimeSpan.FromHours(8) }, null, null, null);
        var oneTime = (string name) => Shift.Create(CompanyA, Guid.NewGuid(), name, new ShiftSchedule { Type = ShiftScheduleType.OneTime, OneTimeDate = new DateTime(2026, 11, 6), OneTimeStart = TimeSpan.FromHours(8), OneTimeEnd = TimeSpan.FromHours(10) }, null, null, null);
        var done = oneTime("Done visit");
        var open = oneTime("Open visit");
        var inactive = Shift.Create(CompanyA, Guid.NewGuid(), "Old contract", new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Friday], WeeklyStart = TimeSpan.FromHours(6), WeeklyEnd = TimeSpan.FromHours(8) }, null, null, null);
        inactive.Deactivate();
        var shifts = new List<Shift> { weekly, done, open, inactive };

        var repo = new Mock<IShiftRepository>();
        repo.Setup(r => r.ListIncludingInactiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(shifts);
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(f => f.ListShiftIdsWithSubmittedFormAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { done.Id });
        return (new ShiftHandlers.ListShiftPageQueryHandler(repo.Object, forms.Object), done);
    }

    [Fact]
    public async Task Counts_cover_every_tab_and_each_tab_lists_only_its_own_shifts()
    {
        var (handler, done) = Build();

        var active = await handler.Handle(new ListShiftPageQuery("active", null, null, null, 0, 25), CancellationToken.None);
        var completed = await handler.Handle(new ListShiftPageQuery("completed", null, null, null, 0, 25), CancellationToken.None);
        var inactive = await handler.Handle(new ListShiftPageQuery("inactive", null, null, null, 0, 25), CancellationToken.None);

        Assert.Equal(new ShiftTabCounts(2, 1, 1), active.Counts);
        Assert.Equal(["Weekly clean", "Open visit"], active.Items.Select(i => i.Name).ToArray());
        Assert.Equal(["Done visit"], completed.Items.Select(i => i.Name).ToArray());
        Assert.True(completed.Items[0].IsCompleted);
        Assert.Equal(done.Id, completed.Items[0].Id);
        Assert.Equal(["Old contract"], inactive.Items.Select(i => i.Name).ToArray());
        Assert.Equal(1, completed.Total);
    }

    [Fact]
    public async Task Search_narrows_the_page_but_not_the_tab_counts()
    {
        var (handler, _) = Build();

        var page = await handler.Handle(new ListShiftPageQuery("active", null, null, "weekly", 0, 25), CancellationToken.None);

        Assert.Equal(["Weekly clean"], page.Items.Select(i => i.Name).ToArray());
        Assert.Equal(1, page.Total);
        Assert.Equal(new ShiftTabCounts(2, 1, 1), page.Counts);
    }

    [Fact]
    public async Task Paging_returns_the_requested_slice_with_the_full_total()
    {
        var (handler, _) = Build();

        var second = await handler.Handle(new ListShiftPageQuery("active", null, null, null, 1, 1), CancellationToken.None);

        Assert.Equal(2, second.Total);
        Assert.Single(second.Items);
        var beyond = await handler.Handle(new ListShiftPageQuery("active", null, null, null, 50, 25), CancellationToken.None);
        Assert.Empty(beyond.Items);
        Assert.Equal(2, beyond.Total);
    }

    [Fact]
    public async Task Company_scope_limits_the_rows_and_the_counts_together()
    {
        var (handler, _) = Build();

        var other = await handler.Handle(new ListShiftPageQuery("active", Guid.NewGuid(), null, null, 0, 25), CancellationToken.None);

        Assert.Empty(other.Items);
        Assert.Equal(new ShiftTabCounts(0, 0, 0), other.Counts);
    }
}
