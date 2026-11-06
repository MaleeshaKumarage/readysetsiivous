using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Domain.Shifts;
using Marten;

namespace CleaningSuite.Application.Notifications;

/// <summary>Uses live effective rosters, never the personal-history projection.</summary>
public sealed class PushReminderScheduler(IPartnerStore store, IShiftRepository shifts,
    IQualityCycleRepository forms, ITenantProfileRepository profiles, ITenantContext tenant,
    IPushOutbox outbox, IShiftWorkReservation reservation, TimeProvider clock, IPushSettings? settings = null) : IPushDeliveryEligibility
{
    public async Task ScheduleAsync(CancellationToken ct = default)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var devices = await store.Session.Query<PushDevice>().Where(x => x.Enabled).ToListAsync(ct);
        if (devices.Count == 0) return;
        var employees = devices.Select(x => x.EmployeeId).ToHashSet();
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var allShifts = await shifts.ListAsync(null, null, ct);
        // One load per roster collection for every shift, instead of three loads per shift on every minute.
        var rosterFrom = now.AddDays(-2); var rosterTo = now.AddMinutes(31);
        var inputs = await shifts.ListRosterInputsAsync(allShifts.Select(x => x.Id).ToArray(), rosterFrom, rosterTo, ct);
        foreach (var shift in allShifts)
        {
            // Include overnight visits and moved visits through the canonical resolver.
            var visits = ShiftRosterReader.ResolveRoster(shift, rosterFrom, rosterTo, zone,
                inputs.Exceptions.GetValueOrDefault(shift.Id) ?? Array.Empty<ShiftOccurrenceException>(),
                inputs.History.GetValueOrDefault(shift.Id) ?? Array.Empty<ShiftAssignment>(),
                inputs.Overrides.GetValueOrDefault(shift.Id) ?? Array.Empty<ShiftOccurrenceAssignment>());
            foreach (var visit in visits.Where(x => !x.IsCancelled))
            foreach (var employee in visit.EmployeeIds.Where(employees.Contains))
            {
                foreach (var kind in VisitReminderKinds)
                {
                    var due = kind switch
                    {
                        "Upcoming" => visit.Occurrence.StartUtc.AddMinutes(-30),
                        "Unfinished" => visit.Occurrence.EndUtc.AddMinutes(30),
                        _ => visit.Occurrence.StartUtc.AddHours(UnstartedHours(kind)),
                    };
                    var expiry = kind == "Upcoming" ? visit.Occurrence.StartUtc : due.AddMinutes(60);
                    if (now < due || now >= expiry) continue;
                    // A new enrollment cannot cause an already-due overdue reminder.
                    if (!devices.Any(x => x.EmployeeId == employee && x.OptedInUtc <= due)) continue;
                    var key = $"{kind}:{shift.Id}:{visit.OriginalStartUtc?.Ticks}:{visit.ExtraExceptionId}:{employee}";
                    var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
                    var existing = await store.Session.LoadAsync<PushIntent>(id, ct);
                    if (existing?.Cancelled == true) continue;
                    var intent = existing ?? new PushIntent { Id = id, EventKey = key, Kind = kind,
                        EmployeeId = employee, ShiftId = shift.Id, OccurredUtc = due };
                    var timingChanged = existing is not null && (intent.OccurrenceStartUtc != visit.Occurrence.StartUtc || intent.OccurrenceEndUtc != visit.Occurrence.EndUtc);
                    intent.OriginalStartUtc = visit.OriginalStartUtc; intent.ExtraExceptionId = visit.ExtraExceptionId;
                    intent.OccurrenceStartUtc = visit.Occurrence.StartUtc; intent.OccurrenceEndUtc = visit.Occurrence.EndUtc;
                    intent.DueUtc = due; intent.ExpiresUtc = expiry;
                    if (await IsEligibleAsync(intent, ct))
                    {
                        if (existing is null) await outbox.StageAsync(store.Session, intent, ct);
                        else
                        {
                            store.Session.Store(intent);
                            var deliveries = await store.Session.Query<PushDelivery>().Where(x => x.IntentId == intent.Id && x.State == "Cancelled").ToListAsync(ct);
                            foreach (var delivery in deliveries.Where(x => timingChanged && x.FailureCode == "ineligible_or_expired" && x.Attempts < 5))
                            {
                                var device = await store.Session.LoadAsync<PushDevice>(delivery.DeviceId, ct);
                                var person = device is null ? null : await store.Session.LoadAsync<CleaningSuite.Domain.Employees.Employee>(device.EmployeeId, ct);
                                if (settings is not { Available: true } || device is not { Enabled: true } || device.VapidKeyId != settings.KeyId
                                    || device.EmployeeId != intent.EmployeeId || device.OptedInUtc > intent.OccurredUtc
                                    || person is not { IsActive: true, IdentitySyncPending: false }
                                    || !CleaningSuite.Domain.Employees.Employee.IsWorkRole(person.Role)
                                    || string.IsNullOrEmpty(device.Subject) || person.KeycloakUserId != device.Subject) continue;
                                delivery.State = "Pending"; delivery.NextAttemptUtc = due; store.Session.Store(delivery);
                            }
                        }
                    }
                }
            }
        }
        await store.Session.SaveChangesAsync(ct);
    }

    /// <summary>Reminders for a scheduled visit that has not started: 1, 12 and 24 hours after its scheduled start.</summary>
    public static readonly string[] VisitReminderKinds = ["Upcoming", "Unfinished", "Unstarted1h", "Unstarted12h", "Unstarted24h"];
    public static bool IsUnstartedKind(string kind) => kind is "Unstarted1h" or "Unstarted12h" or "Unstarted24h";
    private static int UnstartedHours(string kind) => kind switch { "Unstarted1h" => 1, "Unstarted12h" => 12, _ => 24 };

    public async Task<bool> IsEligibleAsync(PushIntent intent, CancellationToken ct = default)
    {
        if (intent.Kind == "WorkAutoAccepted")
        {
            if (!intent.PartnerRequestId.HasValue) return false;
            var request = await store.Session.LoadAsync<CleaningSuite.Domain.Partners.PartnerCleaningRequest>(intent.PartnerRequestId.Value, ct);
            return request is { Status: "Accepted" } && request.AcceptedEmployeeId == intent.EmployeeId
                && request.Revision == intent.RequestRevision;
        }
        if (intent.Kind == "PartnerOffer")
        {
            if (!intent.PartnerRequestId.HasValue) return false;
            var request = await store.Session.LoadAsync<CleaningSuite.Domain.Partners.PartnerCleaningRequest>(intent.PartnerRequestId.Value, ct);
            if (request is not { Status: "Offered", OfferId: not null } || request.OfferedEmployeeId != intent.EmployeeId
                || request.Revision != intent.RequestRevision || request.OfferId != intent.OfferId) return false;
            var branch = await store.Session.LoadAsync<CleaningSuite.Domain.Companies.Branch>(request.BranchId, ct);
            var company = await store.Session.LoadAsync<CleaningSuite.Domain.Companies.Company>(request.CompanyId, ct);
            return branch is { IsActive: true } && branch.CompanyId == request.CompanyId
                && company is { IsActive: true, IsPartnerEnabled: true };
        }
        if (intent.Kind is not ("Upcoming" or "Unfinished") && !IsUnstartedKind(intent.Kind)) return true;
        if (!intent.ShiftId.HasValue || !intent.OccurrenceStartUtc.HasValue) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        if (now < intent.DueUtc || now >= intent.ExpiresUtc) return false;
        var shift = await shifts.GetAsync(intent.ShiftId.Value, ct);
        if (shift is not { IsActive: true }) return false;
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var visits = await shifts.ResolveRosterAsync(shift, intent.OccurrenceStartUtc.Value.AddDays(-1), intent.OccurrenceStartUtc.Value.AddDays(1), zone, ct);
        var visit = visits.SingleOrDefault(x => x.OriginalStartUtc == intent.OriginalStartUtc && x.ExtraExceptionId == intent.ExtraExceptionId);
        if (visit is null || visit.IsCancelled || !visit.EmployeeIds.Contains(intent.EmployeeId)
            || visit.Occurrence.StartUtc != intent.OccurrenceStartUtc || visit.Occurrence.EndUtc != intent.OccurrenceEndUtc) return false;
        var evidence = await forms.GetFormByShiftOccurrenceAsync(shift.Id, intent.EmployeeId, visit.Occurrence.StartUtc, ct);
        if (IsUnstartedKind(intent.Kind))
            // The reminder stops as soon as the visit is started or submitted by this employee.
            return evidence?.StartedAtUtc is null && evidence?.IsSubmitted != true;
        return intent.Kind == "Upcoming"
            ? now < visit.Occurrence.StartUtc && evidence?.StartedAtUtc is null && evidence?.IsSubmitted != true && evidence?.EndedAtUtc is null
            : evidence?.StartedAtUtc is not null && evidence.EndedAtUtc is null && !evidence.IsSubmitted;
    }
}
