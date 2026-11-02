using CleaningSuite.Application.Notifications;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Notifications;
using Marten;

namespace CleaningSuite.Infrastructure.Notifications;

public sealed class PushOutbox : IPushOutbox
{
    public async Task StageScheduleChangeAsync(IDocumentSession session, Guid shiftId, IEnumerable<Guid> employeeIds, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        foreach (var employeeId in employeeIds.Distinct())
        {
            var pending = await session.Query<PushIntent>().Where(i => i.EmployeeId == employeeId && i.ShiftId == shiftId && i.Kind == "ScheduleChanged" && !i.Materialized && !i.Cancelled).ToListAsync(ct);
            foreach (var previous in pending) { previous.Cancelled = true; session.Store(previous); }
            await StageAsync(session, new PushIntent { EmployeeId = employeeId, ShiftId = shiftId, EventKey = $"schedule:{shiftId}:{employeeId}:{Guid.NewGuid()}", OccurredUtc = occurredAt.UtcDateTime, DueUtc = occurredAt.UtcDateTime, ExpiresUtc = occurredAt.UtcDateTime.AddHours(24) }, ct);
        }
    }
    public async Task StagePartnerRequestAsync(IDocumentSession session, Guid requestId, int revision, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        var admins = await session.Query<Employee>().Where(e => e.IsActive && !e.IdentitySyncPending && e.Role == Employee.RoleAdmin).ToListAsync(ct);
        foreach (var admin in admins)
            await StageAsync(session, new PushIntent { EmployeeId = admin.Id, PartnerRequestId = requestId, RequestRevision = revision, AdminAudience = true, Kind = "PartnerRequest", EventKey = $"partner:{requestId}:{revision}:{admin.Id}", OccurredUtc = occurredAt.UtcDateTime, DueUtc = occurredAt.UtcDateTime, ExpiresUtc = occurredAt.UtcDateTime.AddHours(24) }, ct);
    }
    public Task StagePartnerOfferAsync(IDocumentSession session, Guid requestId, int revision, Guid offerId, Guid employeeId, DateTimeOffset occurredAt, CancellationToken ct = default) =>
        StageAsync(session, new PushIntent { EmployeeId = employeeId, PartnerRequestId = requestId, RequestRevision = revision, OfferId = offerId, Kind = "PartnerOffer",
            EventKey = $"partner-offer:{requestId}:{revision}:{offerId}:{employeeId}", OccurredUtc = occurredAt.UtcDateTime,
            DueUtc = occurredAt.UtcDateTime, ExpiresUtc = occurredAt.UtcDateTime.AddHours(24) }, ct);

    public async Task StageAsync(IDocumentSession session, PushIntent intent, CancellationToken ct = default)
    {
        // No device at commit means no event to backfill when a user opts in later.
        var devices = await session.Query<PushDevice>().Where(d => d.EmployeeId == intent.EmployeeId && d.Enabled).ToListAsync(ct);
        if (!devices.Any(d => d.OptedInUtc <= intent.OccurredUtc)) return;
        intent.Id = PushValidation.Id(intent.EventKey);
        if (await session.LoadAsync<PushIntent>(intent.Id, ct) is not null) return;
        session.Store(intent);
    }
}
