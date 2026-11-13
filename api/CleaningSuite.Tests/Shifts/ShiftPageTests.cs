using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

// The page handler reads the tab stored on each shift. This fake repository answers the way the database query does.
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
        // The stored tab and checklist state, as ShiftTabs.StoreAsync writes them in production.
        weekly.Tab = ShiftTabs.Active;
        open.Tab = ShiftTabs.Active;
        done.Tab = ShiftTabs.Completed; done.ChecklistCompleted = true;
        inactive.Tab = ShiftTabs.Inactive;
        var shifts = new List<Shift> { weekly, done, open, inactive };

        var repo = new Mock<IShiftRepository>();
        repo.Setup(r => r.EnsureTabsAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.CountTabsAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid? company, Guid? branch, CancellationToken _) => Task.FromResult(Counts(shifts, company, branch)));
        repo.Setup(r => r.ListTabPageAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<ShiftScheduleType?>()))
            .Returns((string tab, Guid? company, Guid? branch, string? search, int skip, int take, CancellationToken _, ShiftScheduleType? _2) =>
                Task.FromResult(TabPage(shifts, tab, company, branch, search, skip, take)));
        return (new ShiftHandlers.ListShiftPageQueryHandler(repo.Object), done);
    }

    private static IEnumerable<Shift> InScope(List<Shift> shifts, Guid? company, Guid? branch) =>
        shifts.Where(s => (!company.HasValue || s.CompanyId == company) && (!branch.HasValue || s.BranchId == branch));

    private static ShiftTabCounts Counts(List<Shift> shifts, Guid? company, Guid? branch)
    {
        var scope = InScope(shifts, company, branch).ToList();
        return new ShiftTabCounts(scope.Count(s => s.Tab == ShiftTabs.Active), scope.Count(s => s.Tab == ShiftTabs.Completed), scope.Count(s => s.Tab == ShiftTabs.Inactive));
    }

    private static (IReadOnlyList<Shift> Items, int Total) TabPage(List<Shift> shifts, string tab, Guid? company, Guid? branch, string? search, int skip, int take)
    {
        var rows = InScope(shifts, company, branch)
            .Where(s => s.Tab == tab && (search is null || s.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Name).ToList();
        return (rows.Skip(skip).Take(take).ToList(), rows.Count);
    }

    [Fact]
    public async Task Counts_cover_every_tab_and_each_tab_lists_only_its_own_shifts()
    {
        var (handler, done) = Build();

        var active = await handler.Handle(new ListShiftPageQuery("active", null, null, null, 0, 25), CancellationToken.None);
        var completed = await handler.Handle(new ListShiftPageQuery("completed", null, null, null, 0, 25), CancellationToken.None);
        var inactive = await handler.Handle(new ListShiftPageQuery("inactive", null, null, null, 0, 25), CancellationToken.None);

        Assert.Equal(new ShiftTabCounts(2, 1, 1), active.Counts);
        Assert.Equal(["Open visit", "Weekly clean"], active.Items.Select(i => i.Name).ToArray());
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
        Assert.Equal("Weekly clean", second.Items[0].Name);
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

    [Fact]
    public async Task ScheduleTypeFilterIsPassedToTheRepository()
    {
        var repo = new Mock<IShiftRepository>();
        repo.Setup(r => r.EnsureTabsAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.CountTabsAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new ShiftTabCounts(0, 0, 0)));
        repo.Setup(r => r.ListTabPageAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<ShiftScheduleType?>()))
            .Returns(Task.FromResult<(IReadOnlyList<Shift> Items, int Total)>((new List<Shift>(), 0)));
        var handler = new ShiftHandlers.ListShiftPageQueryHandler(repo.Object);

        await handler.Handle(new ListShiftPageQuery("active", null, null, null, 0, 25, ShiftScheduleType.Weekly), CancellationToken.None);

        repo.Verify(r => r.ListTabPageAsync("active", null, null, null, 0, 25, It.IsAny<CancellationToken>(), ShiftScheduleType.Weekly), Times.Once);
    }
}
