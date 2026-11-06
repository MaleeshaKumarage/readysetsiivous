using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.WorkQueue;

/// <summary>Runs the merged work queue query against PostgreSQL, including case-insensitive search.</summary>
public class WorkQueueReaderDatabaseTests
{
    private static WorkQueueReader Reader(Fixture f, ITenantSession session) =>
        new(session, new TenantProfileRepository(f.Store), TenantContext(f.Tenant));

    private static ITenantContext TenantContext(string tenant)
    {
        var mock = new Mock<ITenantContext>();
        mock.SetupGet(t => t.TenantId).Returns(tenant);
        return mock.Object;
    }

    [QualityCycleDatabaseFact]
    public async Task Finished_partner_visit_is_completed_and_cancelled_visits_stay_out_of_completed()
    {
        using var f = await Fixture.Create();
        var lake = new Company { Name = "Lake Owners Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        var shift = Guid.NewGuid();
        var finished = new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = lake.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusAccepted,
            LinkedShiftId = shift, FromDate = new DateOnly(2026, 12, 1), ToDate = new DateOnly(2026, 12, 1) };
        var cancelled = new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = lake.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusCancelled,
            FromDate = new DateOnly(2026, 12, 2), ToDate = new DateOnly(2026, 12, 2) };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(lake);
            db.Store(finished, cancelled);
            db.Store(new QualityCycleForm { ShiftId = shift, EmployeeId = Guid.NewGuid(), IsSubmitted = true,
                StartedAtUtc = new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc), EndedAtUtc = new DateTime(2026, 12, 1, 9, 30, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }

        using var session = f.Session();
        var reader = Reader(f, session);
        var completed = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Completed, "partner", null, 0, 25));
        Assert.Equal(finished.Id, Assert.Single(completed.Items).Id);
        Assert.Equal("Completed", Assert.Single(completed.Items).StateLabel);
        var cancelledPage = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Cancelled, "partner", null, 0, 25));
        Assert.Equal(cancelled.Id, Assert.Single(cancelledPage.Items).Id);
        var scheduled = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "partner", null, 0, 25));
        Assert.Empty(scheduled.Items);
        Assert.Equal(1, completed.Counts[WorkQueueViews.Completed]);
        Assert.Equal(1, completed.Counts[WorkQueueViews.Cancelled]);
    }

    [QualityCycleDatabaseFact]
    public async Task Search_is_case_insensitive_across_bookings_and_partner_companies()
    {
        using var f = await Fixture.Create();
        var lake = new Company { Name = "Lake Owners Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        var forest = new Company { Name = "Forest Homes", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        var start = new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc);
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(lake, forest);
            db.Store(new Booking { BookingNumber = "BK-1001", Customer = new Customer { Name = "Anna Virtanen" }, Status = Booking.StatusNew, StartUtc = start, StartLocalDate = "2026-12-01", StartLocalTime = "10:00" });
            db.Store(new Booking { BookingNumber = "BK-2002", Customer = new Customer { Name = "Ville Lakeside" }, Status = Booking.StatusNew, StartUtc = start.AddDays(1), StartLocalDate = "2026-12-02", StartLocalTime = "10:00" });
            db.Store(new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = lake.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusOffered, FromDate = new DateOnly(2026, 12, 3), ToDate = new DateOnly(2026, 12, 3) });
            db.Store(new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = forest.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusRequested, FromDate = new DateOnly(2026, 12, 4), ToDate = new DateOnly(2026, 12, 4) });
            await db.SaveChangesAsync();
        }

        using var session = f.Session();
        var reader = Reader(f, session);

        var all = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", null, 0, 25));
        Assert.Equal(4, all.Total);

        // "LAKE" matches the Lake Owners partner request and the "Lakeside" booking customer, regardless of case.
        var lakes = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", "LAKE", 0, 25));
        Assert.Equal(2, lakes.Total);
        Assert.Equal(["BK-2002", "Lake Owners Oy"], lakes.Items.Select(i => i.Source == "booking" ? i.Reference : i.CustomerName).Order().ToArray());
        Assert.Equal("Awaiting acceptance", Assert.Single(lakes.Items, i => i.Source == "partner").StateLabel);

        var byNumber = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "booking", "bk-1001", 0, 25));
        Assert.Equal("Anna Virtanen", Assert.Single(byNumber.Items).CustomerName);
    }

    [QualityCycleDatabaseFact]
    public async Task All_sources_merge_on_local_time_and_a_huge_skip_returns_an_empty_page()
    {
        using var f = await Fixture.Create();
        var company = new Company { Name = "Merge Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company);
            // 01:00 Helsinki on 3 Dec is 23:00 UTC on 2 Dec. A UTC key would wrongly put it before the 3 Dec partner request.
            db.Store(new Booking { BookingNumber = "BK-EARLY", Customer = new Customer { Name = "Early bird" }, Status = Booking.StatusNew,
                StartUtc = new DateTime(2026, 12, 2, 23, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-03", StartLocalTime = "01:00" });
            db.Store(new Booking { BookingNumber = "BK-DAY2", Customer = new Customer { Name = "Day two" }, Status = Booking.StatusNew,
                StartUtc = new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-02", StartLocalTime = "10:00" });
            db.Store(new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = company.Id, BranchId = Guid.NewGuid(),
                Status = PartnerCleaningRequest.StatusRequested, FromDate = new DateOnly(2026, 12, 3), ToDate = new DateOnly(2026, 12, 3) });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var reader = Reader(f, session);

        var page = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", null, 0, 25));
        Assert.Equal(["2026-12-02 10:00", "2026-12-03", "2026-12-03 01:00"], page.Items.Select(i => i.ScheduledLocal).ToArray());

        var beyond = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "all", null, int.MaxValue, 100));
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.Total);
    }

    [QualityCycleDatabaseFact]
    public async Task Date_and_assignee_filters_apply_before_paging_and_counts()
    {
        using var f = await Fixture.Create();
        var company = new Company { Name = "Filter Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        var employeeA = Guid.NewGuid().ToString();
        var employeeB = Guid.NewGuid().ToString();
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company);
            // 01:00 Helsinki on 3 Dec is 23:00 UTC on 2 Dec. A date window must use the local day edge, not the UTC date.
            db.Store(new Booking { BookingNumber = "BK-EARLY", Customer = new Customer { Name = "Early bird" }, Status = Booking.StatusConfirmed, EmployeeId = employeeA,
                StartUtc = new DateTime(2026, 12, 2, 23, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-03", StartLocalTime = "01:00" });
            db.Store(new Booking { BookingNumber = "BK-DAY2", Customer = new Customer { Name = "Day two" }, Status = Booking.StatusNew,
                StartUtc = new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-02", StartLocalTime = "10:00" });
            db.Store(new Booking { BookingNumber = "BK-LATE", Customer = new Customer { Name = "Late one" }, Status = Booking.StatusConfirmed, EmployeeId = employeeB,
                StartUtc = new DateTime(2026, 12, 9, 8, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-09", StartLocalTime = "10:00" });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var reader = Reader(f, session);

        var dayThree = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "booking", null, 0, 25, "2026-12-03", "2026-12-03"));
        Assert.Equal(["BK-EARLY"], dayThree.Items.Select(i => i.Reference).ToArray());
        Assert.Equal(0, dayThree.Counts[WorkQueueViews.NeedsAction]);
        Assert.Equal(1, dayThree.Counts[WorkQueueViews.Scheduled]);

        var counts = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "booking", null, 0, 25, "2026-12-01", "2026-12-08"));
        Assert.Equal(1, counts.Total);
        Assert.Equal(1, counts.Counts[WorkQueueViews.Scheduled]);

        var emp = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "all", null, 0, 25, Assignee: employeeB));
        Assert.Equal(["BK-LATE"], emp.Items.Select(i => i.Reference).ToArray());
        Assert.Equal(1, emp.Total);
    }

    [QualityCycleDatabaseFact]
    public async Task Non_guid_assignee_matches_nothing_in_any_source()
    {
        // Bookings and shifts must agree: a non-GUID assignee returns an empty queue, not a partial one.
        using var f = await Fixture.Create();
        var company = new Company { Name = "Assignee Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company);
            db.Store(new Booking { BookingNumber = "BK-ANY", Customer = new Customer { Name = "Anyone" }, Status = Booking.StatusConfirmed, EmployeeId = Guid.NewGuid().ToString(),
                StartUtc = new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-02", StartLocalTime = "10:00" });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var reader = Reader(f, session);

        var result = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "all", null, 0, 25, Assignee: "emp-1"));
        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.Counts[WorkQueueViews.Scheduled]);
    }

    [QualityCycleDatabaseFact]
    public async Task Shift_occurrences_follow_the_assignee_by_guid_and_drop_for_everyone_else()
    {
        // Shift rows must use the same assignee id as bookings: the assigned employee sees the occurrences, others see none.
        using var f = await Fixture.Create();
        var company = new Company { Name = "Roster Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        var assigned = Guid.NewGuid();
        var other = Guid.NewGuid();
        var today = DateTime.UtcNow.Date;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, BranchId = Guid.NewGuid(), Name = "Assigned clean", IsActive = true,
            ValidFrom = today.AddDays(-1), ValidUntil = today.AddDays(30), CreatedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Schedule = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) } };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company);
            db.Store(shift);
            db.Store(new ShiftAssignment { ShiftId = shift.Id, EmployeeId = assigned, AssignedAtUtc = today.AddDays(-10), IsActive = true });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var reader = Reader(f, session);

        var forAssigned = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "shift", null, 0, 25, Assignee: assigned.ToString()));
        // Guards against a vacuous pass: the assigned employee must see at least one occurrence through the GUID match.
        Assert.True(forAssigned.Counts.Values.Sum() > 0, "Assigned employee saw no shift occurrence.");

        var forOther = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "shift", null, 0, 25, Assignee: other.ToString()));
        Assert.Equal(0, forOther.Total);
        Assert.Equal(0, forOther.Counts.Values.Sum());
    }

    [QualityCycleDatabaseFact]
    public async Task Maximum_end_date_does_not_overflow_the_upper_bound()
    {
        // Regression: adding a day to 9999-12-31 threw, so a valid but extreme `to` failed the whole request.
        using var f = await Fixture.Create();
        var company = new Company { Name = "Edge Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company);
            db.Store(new Booking { BookingNumber = "BK-EDGE", Customer = new Customer { Name = "Edge case" }, Status = Booking.StatusConfirmed, EmployeeId = Guid.NewGuid().ToString(),
                StartUtc = new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-02", StartLocalTime = "10:00" });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var reader = Reader(f, session);

        var result = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "booking", null, 0, 25, "2026-12-01", "9999-12-31"));
        Assert.Equal(["BK-EDGE"], result.Items.Select(i => i.Reference).ToArray());
    }

    [QualityCycleDatabaseFact]
    public async Task Minimum_start_date_does_not_underflow_the_lower_bound()
    {
        // Regression: local midnight on 0001-01-01 converted to UTC fell below DateTime.MinValue for tenants east of UTC.
        using var f = await Fixture.Create();
        var company = new Company { Name = "Floor Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company);
            db.Store(new Booking { BookingNumber = "BK-FLOOR", Customer = new Customer { Name = "Floor case" }, Status = Booking.StatusConfirmed, EmployeeId = Guid.NewGuid().ToString(),
                StartUtc = new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Utc), StartLocalDate = "2026-12-02", StartLocalTime = "10:00" });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var reader = Reader(f, session);

        var result = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "booking", null, 0, 25, "0001-01-01", "2026-12-31"));
        Assert.Equal(["BK-FLOOR"], result.Items.Select(i => i.Reference).ToArray());
    }

    [QualityCycleDatabaseFact]
    public async Task Partner_requests_are_kept_when_their_span_overlaps_the_date_window()
    {
        // A request that starts before the window but runs into it must stay; one that starts after the window must go.
        using var f = await Fixture.Create();
        var lake = new Company { Name = "Span Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(lake);
            db.Store(new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = lake.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusRequested,
                FromDate = new DateOnly(2026, 11, 28), ToDate = new DateOnly(2026, 12, 3) });
            db.Store(new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = lake.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusRequested,
                FromDate = new DateOnly(2026, 12, 2), ToDate = new DateOnly(2026, 12, 20) });
            db.Store(new PartnerCleaningRequest { ClientRequestId = Guid.NewGuid(), CompanyId = lake.Id, BranchId = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusRequested,
                FromDate = new DateOnly(2026, 12, 5), ToDate = new DateOnly(2026, 12, 9) });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var reader = Reader(f, session);

        var window = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "partner", null, 0, 25, "2026-12-01", "2026-12-02"));
        Assert.Equal(2, window.Total);
        Assert.Equal(2, window.Counts[WorkQueueViews.NeedsAction]);
        Assert.Equal(2, window.Items.Count);
    }

    [QualityCycleDatabaseFact]
    public async Task Shift_queue_reads_occurrence_forms_with_unspecified_kind_bounds()
    {
        // Regression: UTC-kind window bounds against ShiftOccurrenceUtc made Npgsql reject the query.
        using var f = await Fixture.Create();
        var company = new Company { Name = "Shift Oy", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true, IsPartnerEnabled = true };
        var today = DateTime.UtcNow.Date;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, BranchId = Guid.NewGuid(), Name = "Weekly clean", IsActive = true,
            ValidFrom = today.AddDays(-1), ValidUntil = today.AddDays(30), CreatedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Schedule = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) } };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company);
            db.Store(shift);
            db.Store(new QualityCycleForm { ShiftId = shift.Id, EmployeeId = Guid.NewGuid(), IsSubmitted = true,
                ShiftOccurrenceUtc = DateTime.SpecifyKind(today, DateTimeKind.Unspecified),
                StartedAtUtc = today.AddHours(8), EndedAtUtc = today.AddHours(9) });
            await db.SaveChangesAsync();
        }

        using var session = f.Session();
        var reader = Reader(f, session);
        var page = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "shift", null, 0, 25));
        // Guards against a vacuous pass: the shift must resolve to at least one occurrence and reach classification.
        Assert.True(page.Counts.Values.Sum() > 0, "No shift occurrence classified; shift path not exercised.");
    }

    [QualityCycleDatabaseFact]
    public async Task Future_visits_with_an_assigned_cleaner_are_scheduled_and_only_unassigned_visits_need_action()
    {
        using var f = await Fixture.Create();
        var company = new Company { Name = "Assigned Co", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true };
        var schedule = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = new TimeSpan(8, 0, 0), DailyEnd = new TimeSpan(10, 0, 0) };
        var assigned = Shift.Create(company.Id, Guid.NewGuid(), "Assigned daily", schedule, null, DateTime.UtcNow.Date.AddDays(3), null);
        var open = Shift.Create(company.Id, Guid.NewGuid(), "Open daily", schedule, null, DateTime.UtcNow.Date.AddDays(3), null);
        var cleaner = ShiftAssignment.Create(assigned.Id, Guid.NewGuid(), null);
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company); db.Store(assigned, open); db.Store(cleaner);
            await db.SaveChangesAsync();
        }

        using var session = f.Session();
        var reader = Reader(f, session);
        var needsAction = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "shift", null, 0, 200));
        Assert.All(needsAction.Items, item => Assert.Equal("Unassigned", item.StateLabel));
        Assert.Contains(needsAction.Items, item => item.Reference.Contains("Open daily"));
        Assert.DoesNotContain(needsAction.Items, item => item.Reference.Contains("Assigned daily"));
        var scheduled = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "shift", null, 0, 200));
        Assert.Contains(scheduled.Items, item => item.Reference.Contains("Assigned daily") && item.StateLabel == "Scheduled");
    }

    [QualityCycleDatabaseFact]
    public async Task Long_running_shift_assigned_before_the_window_still_reads_future_visits_as_scheduled()
    {
        using var f = await Fixture.Create();
        var company = new Company { Name = "Long Assigned Co", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true };
        var schedule = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = new TimeSpan(8, 0, 0), DailyEnd = new TimeSpan(10, 0, 0) };
        // Created 40 days ago and assigned then; its future visits must still show the cleaner.
        var started = DateTime.UtcNow.Date.AddDays(-40);
        var assigned = Shift.Create(company.Id, Guid.NewGuid(), "Long assigned daily", schedule, null, started, null);
        var open = Shift.Create(company.Id, Guid.NewGuid(), "Long open daily", schedule, null, started, null);
        var cleaner = ShiftAssignment.Create(assigned.Id, Guid.NewGuid(), null);
        cleaner.AssignedAtUtc = started;
        cleaner.UpdatedUtc = started;
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company); db.Store(assigned, open); db.Store(cleaner);
            await db.SaveChangesAsync();
        }

        using var session = f.Session();
        var reader = Reader(f, session);
        var needsAction = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "shift", null, 0, 200));
        Assert.DoesNotContain(needsAction.Items, item => item.Reference.Contains("Long assigned daily"));
        Assert.Contains(needsAction.Items, item => item.Reference.Contains("Long open daily"));
        var scheduled = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "shift", null, 0, 200));
        Assert.Contains(scheduled.Items, item => item.Reference.Contains("Long assigned daily") && item.StateLabel == "Scheduled");
    }

    [QualityCycleDatabaseFact]
    public async Task One_occurrence_assigned_by_override_is_scheduled_and_the_other_days_stay_unassigned()
    {
        using var f = await Fixture.Create();
        var company = new Company { Name = "Override Co", BusinessId = "WQ-" + Guid.NewGuid().ToString("N")[..8], IsActive = true };
        var schedule = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = new TimeSpan(8, 0, 0), DailyEnd = new TimeSpan(10, 0, 0) };
        var start = DateTime.UtcNow.Date.AddDays(3);
        var shift = Shift.Create(company.Id, Guid.NewGuid(), "Override daily", schedule, null, start, null);
        var cleaner = Guid.NewGuid();
        // The fixture has no tenant profile, so the reader uses the default Helsinki zone: 08:00 local is the override day.
        var helsinki = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
        var localDay = DateTime.SpecifyKind(start.AddDays(1), DateTimeKind.Unspecified);
        var overrideDay = TimeZoneInfo.ConvertTimeToUtc(localDay.AddHours(8), helsinki);
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company); db.Store(shift);
            db.Store(ShiftOccurrenceAssignment.Create(shift.Id, overrideDay, null, OccurrenceAssignmentMode.Replace, [cleaner], "Covered"));
            await db.SaveChangesAsync();
        }

        using var session = f.Session();
        var reader = Reader(f, session);
        var scheduled = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.Scheduled, "shift", null, 0, 200));
        var overrideKey = localDay.ToString("yyyy-MM-dd");
        Assert.Contains(scheduled.Items, item => item.Reference.Contains("Override daily") && item.ScheduledLocal.StartsWith(overrideKey));
        var needsAction = await reader.ReadAsync(new WorkQueueQuery(WorkQueueViews.NeedsAction, "shift", null, 0, 200));
        Assert.DoesNotContain(needsAction.Items, item => item.Reference.Contains("Override daily") && item.ScheduledLocal.StartsWith(overrideKey));
        Assert.Contains(needsAction.Items, item => item.Reference.Contains("Override daily"));
    }

    private sealed class Fixture : IDisposable
    {
        public required ServiceProvider Provider; public required IDocumentStore Store;
        public string Tenant = "work-queue-" + Guid.NewGuid().ToString("N");
        public static async Task<Fixture> Create()
        {
            var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
            var services = new ServiceCollection(); services.AddLogging();
            services.AddCleaningMarten(connection, databaseSchemaName: "work_queue_" + Guid.NewGuid().ToString("N"));
            var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            return new Fixture { Provider = provider, Store = store };
        }
        public TenantSessionFactory Session() { var context = new Mock<ITenantContext>(); context.SetupGet(t => t.TenantId).Returns(Tenant); return new(Store, context.Object); }
        public void Dispose() => Provider.Dispose();
    }
}
