using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using Marten;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>
/// Runs the one-time shift validity correction for every active tenant. Mode "Report" only logs the plan.
/// Mode "Apply" removes the planned records and writes a marker per tenant; tenants with a marker are skipped.
/// </summary>
public sealed class ShiftValidityCorrectionRunner(IDocumentStore store, ITenantRegistry registry, ILogger<ShiftValidityCorrectionRunner> logger)
{
    public const string Key = "shift-validity-window-v1";

    public async Task<IReadOnlyList<(string Tenant, int Removed, int Reported, bool Applied)>> RunAsync(string mode, CancellationToken ct)
    {
        var apply = string.Equals(mode, "Apply", StringComparison.OrdinalIgnoreCase);
        var results = new List<(string, int, int, bool)>();
        foreach (var registration in await registry.ListAsync(ct))
        {
            if (registration.Status != TenantRegistration.StatusActive) continue;
            var tenant = registration.Slug;
            using var session = store.LightweightSession(tenant);
            if (await session.LoadAsync<DataCorrectionRecord>(MarkerId(), ct) is not null) continue;

            var profile = await new TenantProfileRepository(store).GetAsync(tenant, ct);
            var zone = profile is null ? TimeZoneInfo.Utc : TenantTimeZone.Resolve(profile.TimeZoneId);
            var shifts = await session.Query<Shift>().Where(x => x.ValidUntil != null).ToListAsync(ct);
            var ids = shifts.Select(x => x.Id).ToArray();
            var overrides = ids.Length == 0 ? [] : await session.Query<ShiftOccurrenceAssignment>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct);
            var exceptions = ids.Length == 0 ? [] : await session.Query<ShiftOccurrenceException>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct);
            var forms = ids.Length == 0 ? [] : await session.Query<QualityCycleForm>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct);
            var findings = ShiftValidityCorrection.Plan(shifts, overrides, exceptions, forms, zone);
            var removals = findings.Where(x => x.Action == ShiftValidityFindingAction.Remove).ToArray();
            var reported = findings.Count - removals.Length;
            foreach (var finding in findings)
                logger.LogInformation("Shift validity {Mode} tenant {Tenant} shift {ShiftId} {Kind} {Action} at {OccurrenceUtc:o}: {Reason}",
                    apply ? "apply" : "report", tenant, finding.ShiftId, finding.Kind, finding.Action, finding.OccurrenceUtc, finding.Reason);

            if (apply)
            {
                foreach (var removal in removals)
                {
                    if (removal.Kind == ShiftValidityCorrection.RemovedRosterOverride) session.Delete<ShiftOccurrenceAssignment>(removal.DocumentId!.Value);
                    else session.Delete<ShiftOccurrenceException>(removal.DocumentId!.Value);
                }
                session.Store(new DataCorrectionRecord { Id = MarkerId(), Key = Key, AppliedUtc = DateTime.UtcNow, RemovedCount = removals.Length, ReportedCount = reported });
                await session.SaveChangesAsync(ct);
            }
            logger.LogInformation("Shift validity correction tenant {Tenant}: {Removed} removable, {Reported} for review, mode {Mode}", tenant, removals.Length, reported, apply ? "Apply" : "Report");
            results.Add((tenant, removals.Length, reported, apply));
        }
        return results;
    }

    private static Guid MarkerId() => new(SHA256.HashData(Encoding.UTF8.GetBytes("data-correction:" + Key)).AsSpan(0, 16));
}
