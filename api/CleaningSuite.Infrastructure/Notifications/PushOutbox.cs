using CleaningSuite.Application.Notifications;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Notifications;
using Marten;

namespace CleaningSuite.Infrastructure.Notifications;

public sealed class PushOutbox : IPushOutbox
{
    // A newer shift change replaces any not-yet-sent change for the same shift and person, whatever its kind.
    private static readonly string[] ScheduleKinds = ["ShiftAssigned", "ShiftRemoved", "ShiftUpdated", "ScheduleChanged"];

    public async Task StageScheduleChangeAsync(IDocumentSession session, Guid shiftId, IEnumerable<Guid> employeeIds, DateTimeOffset occurredAt, CancellationToken ct = default, string kind = "ShiftUpdated")
    {
        foreach (var employeeId in employeeIds.Distinct())
        {
            var pending = await session.Query<PushIntent>().Where(i => i.EmployeeId == employeeId && i.ShiftId == shiftId && ScheduleKinds.Contains(i.Kind) && !i.Materialized && !i.Cancelled).ToListAsync(ct);
            foreach (var previous in pending) { previous.Cancelled = true; session.Store(previous); }
            await StageAsync(session, new PushIntent { EmployeeId = employeeId, ShiftId = shiftId, Kind = kind, EventKey = $"schedule:{shiftId}:{employeeId}:{Guid.NewGuid()}", OccurredUtc = occurredAt.UtcDateTime, DueUtc = occurredAt.UtcDateTime, ExpiresUtc = occurredAt.UtcDateTime.AddHours(24) }, ct);
        }
    }
    public async Task StagePartnerRequestAsync(IDocumentSession session, Guid requestId, int revision, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        var admins = await session.Query<Employee>().Where(e => e.IsActive && !e.IdentitySyncPending && (e.Role == Employee.RoleAdmin || e.Role == Employee.RoleManager)).ToListAsync(ct);
        foreach (var admin in admins)
            await StageAsync(session, new PushIntent { EmployeeId = admin.Id, PartnerRequestId = requestId, RequestRevision = revision, AdminAudience = true, Kind = "PartnerRequest", EventKey = $"partner:{requestId}:{revision}:{admin.Id}", OccurredUtc = occurredAt.UtcDateTime, DueUtc = occurredAt.UtcDateTime, ExpiresUtc = occurredAt.UtcDateTime.AddHours(24) }, ct);
    }
    public Task StagePartnerOfferAsync(IDocumentSession session, Guid requestId, int revision, Guid offerId, Guid employeeId, DateTimeOffset occurredAt, CancellationToken ct = default) =>
        StageAsync(session, new PushIntent { EmployeeId = employeeId, PartnerRequestId = requestId, RequestRevision = revision, OfferId = offerId, Kind = "PartnerOffer",
            EventKey = $"partner-offer:{requestId}:{revision}:{offerId}:{employeeId}", OccurredUtc = occurredAt.UtcDateTime,
            DueUtc = occurredAt.UtcDateTime, ExpiresUtc = occurredAt.UtcDateTime.AddHours(24) }, ct);

    public async Task StageStaffAlertAsync(IDocumentSession session, string kind, string key, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        var admins = await session.Query<Employee>().Where(e => e.IsActive && !e.IdentitySyncPending && (e.Role == Employee.RoleAdmin || e.Role == Employee.RoleManager)).ToListAsync(ct);
        var copy = CleaningSuite.Application.Notifications.StaffAlertCopy.For(kind);
        foreach (var admin in admins)
        {
            // Switched off in the person's notification settings: nothing is kept or sent for this event.
            if (NotificationPreferenceService.AppTurnedOff(await session.Query<NotificationPreference>().FirstOrDefaultAsync(x => x.EmployeeId == admin.Id, ct), kind)) continue;
            // The in-app inbox keeps the event even when push is off or not yet enabled on a device.
            var inboxId = PushValidation.Id($"inbox:{kind}:{key}:{admin.Id}");
            if (await session.LoadAsync<CleaningSuite.Domain.Notifications.StaffInboxItem>(inboxId, ct) is null)
                session.Store(new CleaningSuite.Domain.Notifications.StaffInboxItem { Id = inboxId, EmployeeId = admin.Id, Kind = kind, Title = copy.Title, Body = copy.Body, Link = copy.Link, CreatedUtc = occurredAt.UtcDateTime, UpdatedUtc = occurredAt.UtcDateTime });
            await StageAsync(session, new PushIntent { EmployeeId = admin.Id, AdminAudience = true, Kind = kind, EventKey = $"staff:{kind}:{key}:{admin.Id}",
                OccurredUtc = occurredAt.UtcDateTime, DueUtc = occurredAt.UtcDateTime, ExpiresUtc = occurredAt.UtcDateTime.AddHours(24) }, ct);
        }
    }

    public async Task StageAsync(IDocumentSession session, PushIntent intent, CancellationToken ct = default)
    {
        // Switched off in the person's notification settings: nothing is queued.
        if (NotificationPreferenceService.AppTurnedOff(await session.Query<NotificationPreference>().FirstOrDefaultAsync(x => x.EmployeeId == intent.EmployeeId, ct), intent.Kind)) return;
        // No device at commit means no event to backfill when a user opts in later.
        var devices = await session.Query<PushDevice>().Where(d => d.EmployeeId == intent.EmployeeId && d.Enabled).ToListAsync(ct);
        if (!devices.Any(d => d.OptedInUtc <= intent.OccurredUtc)) return;
        intent.Id = PushValidation.Id(intent.EventKey);
        if (await session.LoadAsync<PushIntent>(intent.Id, ct) is not null) return;
        session.Store(intent);
    }
}
