using CleaningSuite.Application.Common;
using CleaningSuite.Application.Notifications;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Infrastructure.Notifications;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;

using Fixture = CleaningSuite.Tests.PushReminderDatabaseTests.Fixture;

namespace CleaningSuite.Tests;

[Collection("Database integration")]
public class NotificationPreferenceDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task A_shift_change_switched_off_is_not_queued_and_the_other_kind_still_is()
    {
        using var f = await Fixture.Create();
        var cleaner = new Employee { Role = Employee.RoleEmployee, KeycloakUserId = "cleaner-pref" };
        var shiftId = Guid.NewGuid();
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(cleaner);
            db.Store(new PushDevice { EmployeeId = cleaner.Id, Subject = cleaner.KeycloakUserId, OptedInUtc = DateTime.UtcNow.AddDays(-1) });
            db.Store(new NotificationPreference { Id = NotificationPreference.IdFor(cleaner.Id), EmployeeId = cleaner.Id, AppOff = ["ShiftRemoved"] });
            await db.SaveChangesAsync();
        }
        using (var session = f.Store.LightweightSession(f.Tenant))
        {
            await new PushOutbox().StageScheduleChangeAsync(session, shiftId, [cleaner.Id], DateTimeOffset.UtcNow, default, kind: "ShiftRemoved");
            await new PushOutbox().StageScheduleChangeAsync(session, shiftId, [cleaner.Id], DateTimeOffset.UtcNow, default, kind: "ShiftAssigned");
            await session.SaveChangesAsync();
        }
        using var q = f.Store.QuerySession(f.Tenant);
        var intents = await q.Query<PushIntent>().Where(i => i.EmployeeId == cleaner.Id).ToListAsync();
        var only = Assert.Single(intents);
        Assert.Equal("ShiftAssigned", only.Kind);
    }

    [QualityCycleDatabaseFact]
    public async Task A_staff_alert_switched_off_for_one_person_is_neither_kept_nor_sent_for_them_only()
    {
        using var f = await Fixture.Create();
        var admin = new Employee { Role = Employee.RoleAdmin, KeycloakUserId = "admin-pref" };
        var manager = new Employee { Role = Employee.RoleManager, KeycloakUserId = "manager-pref" };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            db.Store(admin, manager);
            foreach (var person in new[] { admin, manager }) db.Store(new PushDevice { EmployeeId = person.Id, Subject = person.KeycloakUserId, OptedInUtc = DateTime.UtcNow.AddDays(-1) });
            db.Store(new NotificationPreference { Id = NotificationPreference.IdFor(admin.Id), EmployeeId = admin.Id, AppOff = ["StaffVisitOfferDeclined"] });
            await db.SaveChangesAsync();
        }
        using (var session = f.Store.LightweightSession(f.Tenant))
        {
            await new PushOutbox().StageStaffAlertAsync(session, "StaffVisitOfferDeclined", "visit-1", DateTimeOffset.UtcNow);
            await session.SaveChangesAsync();
        }
        using var q = f.Store.QuerySession(f.Tenant);
        var inbox = await q.Query<StaffInboxItem>().Where(i => i.Kind == "StaffVisitOfferDeclined").ToListAsync();
        Assert.Equal(new[] { manager.Id }, inbox.Select(x => x.EmployeeId));
        var intents = await q.Query<PushIntent>().Where(i => i.Kind == "StaffVisitOfferDeclined").ToListAsync();
        Assert.Equal(new[] { manager.Id }, intents.Select(x => x.EmployeeId));
    }

    [QualityCycleDatabaseFact]
    public async Task Preferences_are_per_role_and_unknown_or_foreign_events_are_refused()
    {
        using var f = await Fixture.Create();
        var cleaner = new Employee { Role = Employee.RoleEmployee, KeycloakUserId = "cleaner-svc" };
        using (var db = f.Store.LightweightSession(f.Tenant)) { db.Store(cleaner); await db.SaveChangesAsync(); }
        using var session = f.Session();
        var service = new NotificationPreferenceService(new PartnerStore(session));

        var before = await service.Get(cleaner, default);
        Assert.All(before.Events, e => Assert.True(e.App));
        Assert.All(before.Events, e => Assert.Equal(NotificationEvents.CleanerGroup, e.Group));

        // A cleaner cannot switch off a staff-only event, and an unknown key is not found.
        await Assert.ThrowsAsync<NotFoundException>(() => service.SetApp(cleaner, "StaffPartnerAccepted", false, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.SetApp(cleaner, "NoSuchEvent", false, default));

        var after = await service.SetApp(cleaner, "ShiftRemoved", false, default);
        Assert.False(after.Events.Single(e => e.Key == "ShiftRemoved").App);
        Assert.True(after.Events.Single(e => e.Key == "ShiftAssigned").App);

        var restored = await service.SetApp(cleaner, "ShiftRemoved", true, default);
        Assert.True(restored.Events.Single(e => e.Key == "ShiftRemoved").App);
    }
}
