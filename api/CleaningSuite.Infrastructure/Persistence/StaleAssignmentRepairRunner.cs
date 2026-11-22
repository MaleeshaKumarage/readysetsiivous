using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using Marten;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>
/// Releases active assignments that <see cref="StaleAssignmentRule"/> finds, for every active tenant. Runs on each API
/// start. Released assignments are no longer active, so a later run finds nothing new unless new stale data appears.
/// </summary>
public sealed class StaleAssignmentRepairRunner(IDocumentStore store, ITenantRegistry registry, ILogger<StaleAssignmentRepairRunner> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var total = 0;
        foreach (var registration in await registry.ListAsync(ct))
        {
            if (registration.Status != TenantRegistration.StatusActive) continue;
            var tenant = registration.Slug;
            // The one-time release runs once per tenant; a later start does not repeat it.
            if (await StartupRepairGuard.IsCompleteAsync(store, tenant, StartupRepairMarker.StaleAssignmentRelease, ct)) continue;
            using var session = store.LightweightSession(tenant);

            var shifts = await session.Query<Shift>().ToListAsync(ct);
            var shiftsById = shifts.ToDictionary(shift => shift.Id);
            var requestIds = shifts.Where(shift => shift.PartnerRequestId is not null)
                .Select(shift => shift.PartnerRequestId!.Value).Distinct().ToArray();
            var statuses = requestIds.Length == 0
                ? new Dictionary<Guid, string>()
                : (await session.Query<PartnerCleaningRequest>().Where(request => requestIds.Contains(request.Id)).ToListAsync(ct))
                    .ToDictionary(request => request.Id, request => request.Status);

            var active = await session.Query<ShiftAssignment>().Where(assignment => assignment.IsActive).ToListAsync(ct);
            var stale = StaleAssignmentRule.Find(active, shiftsById, statuses);
            if (stale.Count == 0)
            {
                await StartupRepairGuard.MarkCompleteAsync(store, tenant, StartupRepairMarker.StaleAssignmentRelease, "nothing stale", ct);
                continue;
            }

            var now = DateTime.UtcNow;
            // Snapshot the state before release, so the change can be reversed by hand (#367). Saved in the same transaction.
            var backup = new StaleAssignmentBackup
            {
                TakenUtc = now,
                Assignments = stale.Select(a => new StaleAssignmentSnapshot
                {
                    AssignmentId = a.Id, ShiftId = a.ShiftId, EmployeeId = a.EmployeeId, AssignedAtUtc = a.AssignedAtUtc,
                    IsActive = a.IsActive, Note = a.Note, UnassignedAtUtc = a.UnassignedAtUtc,
                    Periods = a.Periods.Select(p => new ShiftAssignmentPeriod { FromUtc = p.FromUtc, ToUtc = p.ToUtc }).ToList(),
                }).ToList(),
            };
            session.Store(backup);
            foreach (var assignment in stale) assignment.Release(now);
            foreach (var assignment in stale) session.Store(assignment);
            await session.SaveChangesAsync(ct);
            logger.LogInformation("Stale assignment repair tenant {Tenant}: backup {BackupId} holds {Count} prior states", tenant, backup.Id, stale.Count);

            foreach (var assignment in stale)
                logger.LogInformation("Stale assignment released tenant {Tenant} shift {ShiftId} employee {EmployeeId}", tenant, assignment.ShiftId, assignment.EmployeeId);
            logger.LogInformation("Stale assignment repair tenant {Tenant}: {Released} released", tenant, stale.Count);
            await StartupRepairGuard.MarkCompleteAsync(store, tenant, StartupRepairMarker.StaleAssignmentRelease, $"released {stale.Count}", ct);
            total += stale.Count;
        }
        return total;
    }
}
