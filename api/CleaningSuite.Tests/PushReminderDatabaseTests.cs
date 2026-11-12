using CleaningSuite.Application.Notifications;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Notifications;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests;

[Collection("Database integration")]
public class PushReminderDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Daily_recurring_reminders_have_distinct_occurrence_ids_without_history_backfill()
    {
        using var f = await Fixture.Create();
        f.Shift.Schedule = new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(10), DailyEnd = TimeSpan.FromHours(11) };
        f.Now = new(2026, 11, 2, 7, 30, 0, TimeSpan.Zero); // Helsinki 09:30.
        await f.Scheduler().ScheduleAsync();
        using (var q = f.Store.QuerySession(f.Tenant)) Assert.Single(await q.Query<PushIntent>().ToListAsync());
        f.Now = f.Now.AddDays(1); await f.Scheduler().ScheduleAsync();
        using var query = f.Store.QuerySession(f.Tenant);
        var intents = await query.Query<PushIntent>().ToListAsync();
        Assert.Equal(2, intents.Count); Assert.Equal(2, intents.Select(x => x.Id).Distinct().Count());
        Assert.All(intents, x => Assert.Equal("Upcoming", x.Kind));
    }

    [QualityCycleDatabaseFact]
    public async Task Unstarted_visit_reminders_fire_once_at_one_twelve_and_twenty_four_hours_after_start()
    {
        using var f = await Fixture.Create();
        f.Shift.Schedule = new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(10), DailyEnd = TimeSpan.FromHours(11) };
        // Helsinki is UTC+2 in November: the 10:00 visit starts at 08:00 UTC.
        async Task<string[]> UnstartedKindsAt(DateTime utc)
        {
            f.Now = utc; await f.Scheduler().ScheduleAsync();
            using var query = f.Store.QuerySession(f.Tenant);
            // Only the 2 Nov visit: the 1 Nov visit's own reminders are due at other times.
            var visitStart = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
            return (await query.Query<PushIntent>().ToListAsync()).Where(x => x.Kind.StartsWith("Unstarted") && x.OccurrenceStartUtc == visitStart)
                .Select(x => x.Kind).OrderBy(k => k).ToArray();
        }
        Assert.Empty(await UnstartedKindsAt(new(2026, 11, 2, 8, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new[] { "Unstarted1h" }, await UnstartedKindsAt(new(2026, 11, 2, 9, 1, 0, DateTimeKind.Utc)));
        Assert.Equal(new[] { "Unstarted12h", "Unstarted1h" }, await UnstartedKindsAt(new(2026, 11, 2, 20, 1, 0, DateTimeKind.Utc)));
        Assert.Equal(new[] { "Unstarted12h", "Unstarted1h", "Unstarted24h" }, await UnstartedKindsAt(new(2026, 11, 3, 8, 1, 0, DateTimeKind.Utc)));
    }

    [QualityCycleDatabaseFact]
    public async Task Enrollment_after_due_time_does_not_backfill_unfinished_or_old_assignments()
    {
        using var f = await Fixture.Create();
        f.AddExtra(f.Now.UtcDateTime.AddHours(-2), f.Now.UtcDateTime.AddMinutes(-31));
        f.Evidence = new() { StartedAtUtc = f.Now.UtcDateTime.AddHours(-2) };
        using (var db = f.Store.LightweightSession(f.Tenant))
        { var device = (await db.Query<PushDevice>().SingleAsync()); device.OptedInUtc = f.Now.UtcDateTime; db.Store(device); await db.SaveChangesAsync(); }
        await f.Scheduler().ScheduleAsync();
        using var q = f.Store.QuerySession(f.Tenant); Assert.Empty(await q.Query<PushIntent>().ToListAsync());
    }

    [QualityCycleDatabaseFact]
    public async Task A_second_device_enrolled_after_due_is_excluded_during_worker_catchup()
    {
        using var f = await Fixture.Create();
        var due = f.Now.UtcDateTime.AddMinutes(-5);
        f.AddExtra(due.AddMinutes(30), due.AddMinutes(90));
        var late = new PushDevice { EmployeeId = f.Employee.Id, Subject = "subject", OptedInUtc = due.AddMinutes(1), CreatedUtc = due.AddMinutes(1) };
        using (var db = f.Store.LightweightSession(f.Tenant)) { db.Store(late); await db.SaveChangesAsync(); }
        await f.Scheduler().ScheduleAsync();
        using var session = f.Session();
        var settings = Mock.Of<IPushSettings>(x => x.Available == true && x.KeyId == "key");
        var transport = new Mock<IPushTransport>();
        var eligibility = new Mock<IPushDeliveryEligibility>();
        eligibility.Setup(x => x.IsEligibleAsync(It.IsAny<PushIntent>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var clock = new Mock<TimeProvider>(); clock.Setup(x => x.GetUtcNow()).Returns(f.Now);
        await new PushDeliveryService(session, new PushDeliveryReservation(f.Config, session), settings, transport.Object, eligibility.Object, clock.Object).ProcessAsync();
        using var q = f.Store.QuerySession(f.Tenant);
        var intent = await q.Query<PushIntent>().SingleAsync(); Assert.Equal(due, intent.OccurredUtc);
        var delivery = Assert.Single(await q.Query<PushDelivery>().ToListAsync());
        Assert.Equal(f.Device.Id, delivery.DeviceId); Assert.NotEqual(late.Id, delivery.DeviceId);
    }

    [QualityCycleDatabaseFact]
    public async Task Moved_pending_occurrence_keeps_id_updates_due_and_accepted_delivery_is_not_repeated()
    {
        using var f = await Fixture.Create();
        var start = f.Now.UtcDateTime.AddMinutes(30); var visit = f.AddExtra(start, start.AddHours(1));
        await f.Scheduler().ScheduleAsync();
        Guid intentId; Guid deliveryId;
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            var intent = await db.Query<PushIntent>().SingleAsync(); intentId = intent.Id;
            intent.Materialized = true; db.Store(intent);
            var delivery = new PushDelivery { IntentId = intent.Id, DeviceId = f.Device.Id, State = "Accepted", Attempts = 1 };
            deliveryId = delivery.Id; db.Store(delivery); await db.SaveChangesAsync();
        }
        visit.StartUtc = start.AddDays(1); visit.EndUtc = visit.StartUtc.AddHours(1);
        f.Now = f.Now.AddDays(1); await f.Scheduler().ScheduleAsync();
        using var q = f.Store.QuerySession(f.Tenant);
        var current = await q.Query<PushIntent>().SingleAsync();
        Assert.Equal(intentId, current.Id); Assert.Equal(visit.StartUtc.AddMinutes(-30), current.DueUtc);
        Assert.Equal("Accepted", (await q.LoadAsync<PushDelivery>(deliveryId))!.State);
        Assert.Equal(visit.StartUtc, current.OccurrenceStartUtc);
    }

    [QualityCycleDatabaseFact]
    public async Task Cancelled_reminder_revival_requires_changed_time_and_current_device()
    {
        foreach (var mode in new[] { "unchanged", "moved", "disabled", "rotated", "inactive", "late" })
        {
            using var f = await Fixture.Create();
            var start = f.Now.UtcDateTime.AddMinutes(30);
            var visit = f.AddExtra(start, start.AddHours(1));
            await f.Scheduler().ScheduleAsync();
            Guid deliveryId;
            using (var db = f.Store.LightweightSession(f.Tenant))
            {
                var intent = await db.Query<PushIntent>().SingleAsync(); intent.Materialized = true; db.Store(intent);
                var delivery = new PushDelivery { IntentId = intent.Id, DeviceId = f.Device.Id, State = "Cancelled", FailureCode = "ineligible_or_expired" };
                deliveryId = delivery.Id; db.Store(delivery);
                f.Device.VapidKeyId = mode == "rotated" ? "old-key" : "key";
                f.Device.Enabled = mode != "disabled";
                if (mode == "late") f.Device.OptedInUtc = f.Now.UtcDateTime.AddHours(1);
                f.Employee.IsActive = mode != "inactive";
                db.Store(f.Device); db.Store(f.Employee); await db.SaveChangesAsync();
            }
            if (mode != "unchanged") { visit.StartUtc = start.AddDays(1); visit.EndUtc = visit.StartUtc.AddHours(1); f.Now = f.Now.AddDays(1); }
            await f.Scheduler().ScheduleAsync();
            await f.Scheduler().ScheduleAsync();
            using var query = f.Store.QuerySession(f.Tenant);
            Assert.Equal(mode == "moved" ? "Pending" : "Cancelled", (await query.LoadAsync<PushDelivery>(deliveryId))!.State);
        }
    }

    [QualityCycleDatabaseFact]
    public async Task Removed_and_cancelled_visits_create_no_reminders()
    {
        using var f = await Fixture.Create();
        var visit = f.AddExtra(f.Now.UtcDateTime.AddMinutes(30), f.Now.UtcDateTime.AddMinutes(90));
        visit.EmployeeId = Guid.NewGuid(); f.Assignments.Clear();
        await f.Scheduler().ScheduleAsync();
        visit.Status = ShiftOccurrenceExceptionStatus.CompanyCancelled; visit.OriginalStartUtc = visit.StartUtc;
        await f.Scheduler().ScheduleAsync();
        using var q = f.Store.QuerySession(f.Tenant); Assert.Empty(await q.Query<PushIntent>().ToListAsync());
    }

    [QualityCycleDatabaseFact]
    public async Task New_partner_request_targets_only_current_opted_in_synced_administrators_and_managers_and_retry_is_idempotent()
    {
        using var f = await Fixture.Create();
        var admin = new Employee { Role = Employee.RoleAdmin, KeycloakUserId = "admin", ShiftCalendarEnabled = false };
        var manager = new Employee { Role = Employee.RoleManager, KeycloakUserId = "manager" };
        var pending = new Employee { Role = Employee.RoleAdmin, KeycloakUserId = "pending", IdentitySyncPending = true };
        var company = new Company { IsActive = true, IsPartnerEnabled = true, BusinessId = "1234567-1" };
        var partner = new Employee { Role = Employee.RolePartner, KeycloakUserId = "partner", PartnerCompanyId = company.Id };
        var branch = new Branch { CompanyId = company.Id, IsActive = true, PriceNet = 100, VatRatePercent = 25.5m, EstimatedDurationMinutes = 60, SizeM2 = 34 };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(admin, manager, pending, partner); db.Store(company); db.Store(branch);
            foreach (var person in new[] { admin, manager, pending }) db.Store(new PushDevice { EmployeeId = person.Id, Subject = person.KeycloakUserId, OptedInUtc = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var service = new PartnerService(new PartnerStore(session), new ShiftWorkReservation(f.Config, session), f.Shifts.Object,
            Mock.Of<ITenantProfileRepository>(), f.Context, new PushOutbox());
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var body = new CreatePartnerRequest(Guid.NewGuid(), branch.Id, tomorrow, tomorrow);
        await service.Create(partner.Id, body, default); await service.Create(partner.Id, body, default);
        using var q = f.Store.QuerySession(f.Tenant);
        var intents = await q.Query<PushIntent>().ToListAsync();
        Assert.Equal(new[] { admin.Id, manager.Id }.Order(), intents.Select(x => x.EmployeeId).Order());
        Assert.All(intents, x => { Assert.True(x.AdminAudience); Assert.Equal("PartnerRequest", x.Kind); });
    }

    [QualityCycleDatabaseFact]
    public async Task Staff_alerts_reach_only_opted_in_synced_administrators_and_managers_once_per_event()
    {
        using var f = await Fixture.Create();
        var admin = new Employee { Role = Employee.RoleAdmin, KeycloakUserId = "admin-alert" };
        var manager = new Employee { Role = Employee.RoleManager, KeycloakUserId = "manager-alert" };
        var pending = new Employee { Role = Employee.RoleAdmin, KeycloakUserId = "pending-alert", IdentitySyncPending = true };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(admin, manager, pending);
            foreach (var person in new[] { admin, manager, pending }) db.Store(new PushDevice { EmployeeId = person.Id, Subject = person.KeycloakUserId, OptedInUtc = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        using (var session = f.Store.LightweightSession(f.Tenant))
        {
            await new PushOutbox().StageStaffAlertAsync(session, "StaffPartnerAccepted", "request-1:2", DateTimeOffset.UtcNow);
            await session.SaveChangesAsync();
            // The same event staged again is not sent twice.
            await new PushOutbox().StageStaffAlertAsync(session, "StaffPartnerAccepted", "request-1:2", DateTimeOffset.UtcNow);
            await session.SaveChangesAsync();
        }
        using var q = f.Store.QuerySession(f.Tenant);
        var intents = await q.Query<PushIntent>().Where(i => i.Kind == "StaffPartnerAccepted").ToListAsync();
        Assert.Equal(new[] { admin.Id, manager.Id }.Order(), intents.Select(x => x.EmployeeId).Order());
        Assert.All(intents, x => Assert.True(x.AdminAudience));
        // The inbox keeps the same event for the same people, once, even without push enabled.
        using var inboxQuery = f.Store.QuerySession(f.Tenant);
        var inbox = await inboxQuery.Query<CleaningSuite.Domain.Notifications.StaffInboxItem>().Where(i => i.Kind == "StaffPartnerAccepted").ToListAsync();
        Assert.Equal(new[] { admin.Id, manager.Id }.Order(), inbox.Select(x => x.EmployeeId).Order());
        Assert.All(inbox, x => Assert.Equal("Cleaner accepted partner work", x.Title));
    }

    [QualityCycleDatabaseFact]
    public async Task Original_key_reassignment_notifies_previous_exception_only_assignee()
    { await VerifyExceptionReassignment(false); await VerifyExceptionReassignment(true); }

    private async Task VerifyExceptionReassignment(bool loadByList)
    {
        using var f = await Fixture.Create();
        var oldEmployee = Guid.NewGuid(); var newEmployee = Guid.NewGuid();
        f.Shift.Schedule = new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(10), DailyEnd = TimeSpan.FromHours(11) };
        var day = f.Now.UtcDateTime.Date.AddDays(1);
        // Use a real scheduled key at whole-second precision, as PostgreSQL timestamp
        // comparisons cannot preserve arbitrary DateTime.UtcNow sub-microsecond ticks.
        var start = Assert.Single(ShiftScheduleCalculator.GenerateOccurrences(f.Shift, day, day, TenantTimeZone.Resolve(null))).StartUtc;
        var exception = ShiftOccurrenceException.Create(f.Shift.Id, start, start.AddHours(1), start.AddHours(2), ShiftOccurrenceExceptionStatus.Moved, "move", oldEmployee);
        using (var db = f.Store.LightweightSession(f.Tenant)) { db.Store(f.Shift); db.Store(exception); await db.SaveChangesAsync(); }
        using var session = f.Session(); var outbox = new Mock<IPushOutbox>();
        Guid[] recipients = [];
        outbox.Setup(x => x.StageScheduleChangeAsync(It.IsAny<IDocumentSession>(), f.Shift.Id, It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<IDocumentSession, Guid, IEnumerable<Guid>, DateTimeOffset, CancellationToken, string, string?>((_, _, ids, _, _, _, _) => recipients = ids.ToArray()).Returns(Task.CompletedTask);
        var repository = new ShiftRepository(session, outbox.Object);
        var tracked = loadByList
            ? (await repository.ListOccurrenceExceptionsAsync(f.Shift.Id, start.AddDays(-1), start.AddDays(2))).Single()
            : await repository.GetOccurrenceExceptionByOriginalAsync(f.Shift.Id, start);
        Assert.NotNull(tracked);
        tracked.EmployeeId = newEmployee;
        await repository.SaveOccurrenceExceptionAsync(tracked);
        Assert.Contains(oldEmployee, recipients); Assert.Contains(newEmployee, recipients);
    }

    [QualityCycleDatabaseFact]
    public async Task Single_visit_override_notifies_only_effective_before_and_after_rosters()
    {
        using var f = await Fixture.Create();
        var original = DateTime.UtcNow.AddDays(1);
        var oldEmployee = Guid.NewGuid(); var newEmployee = Guid.NewGuid(); var unrelated = Guid.NewGuid();
        var visit = ShiftOccurrenceException.Create(f.Shift.Id, null, original, original.AddHours(1), ShiftOccurrenceExceptionStatus.Extra, "extra", oldEmployee);
        var elsewhere = ShiftOccurrenceException.Create(f.Shift.Id, null, original.AddDays(1), original.AddDays(1).AddHours(1), ShiftOccurrenceExceptionStatus.Extra, "elsewhere", unrelated);
        var change = ShiftOccurrenceAssignment.Create(f.Shift.Id, null, visit.Id, OccurrenceAssignmentMode.Replace, [oldEmployee], "before");
        using (var db = f.Store.LightweightSession(f.Tenant))
        { db.Store(f.Shift); db.Store(visit, elsewhere); db.Store(change); db.Store(f.Assignments.ToArray()); await db.SaveChangesAsync(); }
        using var session = f.Session(); var outbox = new Mock<IPushOutbox>(); Guid[] recipients = [];
        outbox.Setup(x => x.StageScheduleChangeAsync(It.IsAny<IDocumentSession>(), f.Shift.Id, It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<IDocumentSession, Guid, IEnumerable<Guid>, DateTimeOffset, CancellationToken, string, string?>((_, _, ids, _, _, _, _) => recipients = ids.Distinct().ToArray()).Returns(Task.CompletedTask);
        var repository = new ShiftRepository(session, outbox.Object);
        var tracked = (await repository.ListOccurrenceAssignmentsAsync(f.Shift.Id)).Single();
        tracked.Update(OccurrenceAssignmentMode.Replace, [newEmployee], "after");
        await repository.SaveOccurrenceAssignmentAsync(tracked);
        Assert.Equal(new[] { oldEmployee, newEmployee }.Order(), recipients.Order());
        Assert.DoesNotContain(unrelated, recipients); Assert.DoesNotContain(f.Employee.Id, recipients);
    }

    [QualityCycleDatabaseFact]
    public Task Manual_offer_commits_one_approval_push() => AssertOfferPush(false);

    [QualityCycleDatabaseFact]
    public Task Automatic_offer_commits_only_one_informational_push() => AssertOfferPush(true);

    private async Task AssertOfferPush(bool automatic)
    {
        using var f = await Fixture.Create();
        f.Employee.AutoAcceptWork = automatic;
        var admin = new Employee { Role = Employee.RoleAdmin, KeycloakUserId = "admin" };
        var company = new Company { IsActive = true, IsPartnerEnabled = true };
        var branch = new Branch { CompanyId = company.Id, IsActive = true, SizeM2 = 34 };
        var request = new CleaningSuite.Domain.Partners.PartnerCleaningRequest
        {
            CompanyId = company.Id, BranchId = branch.Id,
            FromDate = new(2026, 11, 3), ToDate = new(2026, 11, 3), EstimatedDurationMinutes = 60
        };
        using (var db = f.Store.LightweightSession(f.Tenant))
        { db.Store(f.Employee); db.Store(admin); db.Store(company); db.Store(branch); db.Store(request); await db.SaveChangesAsync(); }
        using var session = f.Session();
        var service = new PartnerService(new PartnerStore(session), new ShiftWorkReservation(f.Config, session), f.Shifts.Object,
            Mock.Of<ITenantProfileRepository>(), f.Context, new PushOutbox());

        await service.Offer(admin.Id, request.Id, new PartnerOffer(request.Revision, f.Employee.Id, "2026-11-03T10:00"), default);

        using var query = f.Store.QuerySession(f.Tenant);
        var committed = await query.LoadAsync<CleaningSuite.Domain.Partners.PartnerCleaningRequest>(request.Id);
        Assert.NotNull(committed);
        Assert.Equal(automatic ? "Accepted" : "Offered", committed.Status);
        var intent = Assert.Single(await query.Query<PushIntent>().ToListAsync());
        Assert.Equal(automatic ? "WorkAutoAccepted" : "PartnerOffer", intent.Kind);
        Assert.Equal(f.Employee.Id, intent.EmployeeId);
        Assert.Equal(request.Id, intent.PartnerRequestId);
        Assert.Equal(committed.Revision, intent.RequestRevision);
        if (!automatic) Assert.Equal(committed.OfferId, intent.OfferId);
        Assert.False(intent.AdminAudience);
    }

    [QualityCycleDatabaseFact]
    public async Task Direct_partner_offer_is_generic_idempotent_and_ineligible_after_response()
    {
        using var f = await Fixture.Create();
        var company = new Company { IsActive = true, IsPartnerEnabled = true };
        var branch = new Branch { CompanyId = company.Id, IsActive = true, SizeM2 = 34 };
        var request = new CleaningSuite.Domain.Partners.PartnerCleaningRequest { CompanyId = company.Id, BranchId = branch.Id, Status = "Offered", OfferedEmployeeId = f.Employee.Id, OfferId = Guid.NewGuid() };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(company); db.Store(branch); db.Store(request);
            var outbox = new PushOutbox();
            await outbox.StagePartnerOfferAsync(db, request.Id, request.Revision, request.OfferId!.Value, f.Employee.Id, f.Now);
            await db.SaveChangesAsync();
            await outbox.StagePartnerOfferAsync(db, request.Id, request.Revision, request.OfferId!.Value, f.Employee.Id, f.Now);
            await db.SaveChangesAsync();
        }
        PushIntent intent;
        using (var q = f.Store.QuerySession(f.Tenant)) intent = Assert.Single(await q.Query<PushIntent>().ToListAsync());
        Assert.Equal("PartnerOffer", intent.Kind); Assert.False(intent.AdminAudience);
        Assert.True(await f.Scheduler().IsEligibleAsync(intent));
        using (var db = f.Store.LightweightSession(f.Tenant)) { request.Status = "Accepted"; db.Store(request); await db.SaveChangesAsync(); }
        Assert.False(await f.Scheduler().IsEligibleAsync(intent));
        request.Status = "Offered"; request.Revision++; request.OfferId = Guid.NewGuid();
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            // Stage explicitly supplied revision/offer facts before the changed request is stored.
            await new PushOutbox().StagePartnerOfferAsync(db, request.Id, request.Revision, request.OfferId.Value, f.Employee.Id, f.Now);
            db.Store(request); await db.SaveChangesAsync();
        }
        using var query = f.Store.QuerySession(f.Tenant);
        var offers = await query.Query<PushIntent>().Where(x => x.Kind == "PartnerOffer").ToListAsync();
        Assert.Equal(2, offers.Count);
        var fresh = offers.Single(x => x.RequestRevision == request.Revision);
        Assert.NotEqual(intent.Id, fresh.Id);
        Assert.False(await f.Scheduler().IsEligibleAsync(intent));
        Assert.True(await f.Scheduler().IsEligibleAsync(fresh));
        // Offer identity also invalidates an old delivery without a revision change.
        using (var db = f.Store.LightweightSession(f.Tenant))
        { request.OfferId = Guid.NewGuid(); db.Store(request); await db.SaveChangesAsync(); }
        Assert.False(await f.Scheduler().IsEligibleAsync(fresh));
    }

    [QualityCycleDatabaseFact]
    public async Task Administrator_fallback_uses_committed_revision_and_retries_deduplicate()
    {
        using var f = await Fixture.Create();
        var admin = new Employee { Role = Employee.RoleAdmin, KeycloakUserId = "admin" };
        var request = new CleaningSuite.Domain.Partners.PartnerCleaningRequest();
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(admin); db.Store(request);
            db.Store(new PushDevice { EmployeeId = admin.Id, Subject = "admin", OptedInUtc = f.Now.UtcDateTime.AddDays(-1) });
            await db.SaveChangesAsync();
            var outbox = new PushOutbox();
            await outbox.StagePartnerRequestAsync(db, request.Id, 1, f.Now);
            await db.SaveChangesAsync();
            request.Revision = 3;
            await outbox.StagePartnerRequestAsync(db, request.Id, request.Revision, f.Now);
            db.Store(request); await db.SaveChangesAsync();
            await outbox.StagePartnerRequestAsync(db, request.Id, request.Revision, f.Now);
            await db.SaveChangesAsync();
        }
        using var query = f.Store.QuerySession(f.Tenant);
        var notices = await query.Query<PushIntent>().Where(x => x.Kind == "PartnerRequest").ToListAsync();
        Assert.Equal(2, notices.Count);
        Assert.Equal(new int?[] { 1, 3 }, notices.OrderBy(x => x.RequestRevision).Select(x => x.RequestRevision));
        Assert.All(notices, x => { Assert.Equal(admin.Id, x.EmployeeId); Assert.True(x.AdminAudience); });
    }

    [QualityCycleDatabaseFact]
    public async Task Automatic_acceptance_push_requires_current_accepted_employee_and_revision()
    {
        using var f = await Fixture.Create();
        var request = new CleaningSuite.Domain.Partners.PartnerCleaningRequest { Status = "Accepted", AcceptedEmployeeId = f.Employee.Id, Revision = 3 };
        var intent = new PushIntent { Kind = "WorkAutoAccepted", EmployeeId = f.Employee.Id, PartnerRequestId = request.Id, RequestRevision = 3 };
        async Task Save() { using var db = f.Store.LightweightSession(f.Tenant); db.Store(request); await db.SaveChangesAsync(); }
        await Save();
        Assert.True(await f.Scheduler().IsEligibleAsync(intent));
        request.Revision++; await Save();
        Assert.False(await f.Scheduler().IsEligibleAsync(intent));
        request.Revision = 3; request.AcceptedEmployeeId = Guid.NewGuid(); await Save();
        Assert.False(await f.Scheduler().IsEligibleAsync(intent));
        request.AcceptedEmployeeId = f.Employee.Id; request.Status = "Offered"; await Save();
        Assert.False(await f.Scheduler().IsEligibleAsync(intent));
    }

    internal sealed class Fixture : IDisposable
    {
        public required ServiceProvider Provider; public required IDocumentStore Store; public required IConfiguration Config;
        public string Tenant = "push-reminder-" + Guid.NewGuid().ToString("N");
        public DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        public Employee Employee = new() { KeycloakUserId = "subject" };
        public PushDevice Device = new();
        public Shift Shift = new() { IsActive = true, Schedule = new() { Type = ShiftScheduleType.OnCallFlexible } };
        public List<ShiftOccurrenceException> Exceptions = []; public List<ShiftAssignment> Assignments = [];
        public QualityCycleForm? Evidence;
        public Mock<IShiftRepository> Shifts = new();
        public ITenantContext Context => Mock.Of<ITenantContext>(x => x.TenantId == Tenant);
        public static async Task<Fixture> Create()
        {
            var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
            var services = new ServiceCollection(); services.AddLogging(); services.AddTestMarten(connection, databaseSchemaName: "push_reminder_" + Guid.NewGuid().ToString("N"));
            var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>(); await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            var f = new Fixture { Provider = provider, Store = store, Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build() };
            f.Device = new() { EmployeeId = f.Employee.Id, Subject = "subject", OptedInUtc = f.Now.UtcDateTime.AddDays(-2), CreatedUtc = f.Now.UtcDateTime.AddDays(-2) };
            using var db = store.LightweightSession(f.Tenant); db.Store(f.Employee); db.Store(f.Device); await db.SaveChangesAsync();
            f.Assignments.Add(new() { ShiftId = f.Shift.Id, EmployeeId = f.Employee.Id, IsActive = true, AssignedAtUtc = f.Now.UtcDateTime.AddYears(-1) });
            f.Shifts.Setup(x => x.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync([f.Shift]);
            f.Shifts.Setup(x => x.GetAsync(f.Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(f.Shift);
            f.Shifts.Setup(x => x.ListOccurrenceExceptionsAsync(f.Shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Exceptions);
            f.Shifts.Setup(x => x.ListAssignmentHistoryAsync(f.Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Assignments);
            f.Shifts.Setup(x => x.ListOccurrenceAssignmentsAsync(f.Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            f.Shifts.Setup(x => x.ListRosterInputsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ShiftRosterInputs(new Dictionary<Guid, IReadOnlyList<ShiftOccurrenceException>> { [f.Shift.Id] = f.Exceptions },
                    new Dictionary<Guid, IReadOnlyList<ShiftAssignment>> { [f.Shift.Id] = f.Assignments }, new Dictionary<Guid, IReadOnlyList<ShiftOccurrenceAssignment>>()));
            return f;
        }
        public ShiftOccurrenceException AddExtra(DateTime start, DateTime end)
        { var visit = ShiftOccurrenceException.Create(Shift.Id, null, start, end, ShiftOccurrenceExceptionStatus.Extra, "test", Employee.Id); Exceptions.Add(visit); return visit; }
        private readonly System.Collections.Concurrent.ConcurrentBag<TenantSessionFactory> ownedSessions = [];
        public TenantSessionFactory Session()
        {
            var session = new TenantSessionFactory(Store, Context);
            ownedSessions.Add(session);
            return session;
        }
        public PushReminderScheduler Scheduler()
        {
            var session = Session(); var forms = new Mock<IQualityCycleRepository>();
            forms.Setup(x => x.GetFormByShiftOccurrenceAsync(Shift.Id, Employee.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Evidence);
            var clock = new Mock<TimeProvider>(); clock.Setup(x => x.GetUtcNow()).Returns(() => Now);
            return new(new PartnerStore(session), Shifts.Object, forms.Object, Mock.Of<ITenantProfileRepository>(), Context,
                new PushOutbox(), new ShiftWorkReservation(Config, session), clock.Object,
                Mock.Of<IPushSettings>(x => x.Available == true && x.KeyId == "key"));
        }
        public void Dispose()
        {
            foreach (var session in ownedSessions) session.Dispose();
            Provider.Dispose();
        }
    }
}
