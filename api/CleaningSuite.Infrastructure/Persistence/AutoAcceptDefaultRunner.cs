using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using Marten;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>
/// One-time release decision: every existing work employee (admin, manager, employee) moves to automatic acceptance,
/// including people who switched it off. Partner accounts are not touched. Runs once per active tenant. The marker is
/// written in the same commit as the change, so later starts skip the tenant and keep choices made afterwards.
/// Mirrors api/deploy/employee-auto-accept-migration.sql.
/// </summary>
public sealed class AutoAcceptDefaultRunner(IDocumentStore store, ITenantRegistry registry, ILogger<AutoAcceptDefaultRunner> logger)
{
    public const string Key = "employee-auto-accept-default-r05";

    public async Task<IReadOnlyList<(string Tenant, int Switched)>> RunAsync(CancellationToken ct)
    {
        var results = new List<(string, int)>();
        foreach (var registration in await registry.ListAsync(ct))
        {
            if (registration.Status != TenantRegistration.StatusActive) continue;
            var tenant = registration.Slug;
            using var session = store.LightweightSession(tenant);
            if (await session.LoadAsync<DataCorrectionRecord>(MarkerId(), ct) is not null) continue;

            var employees = await session.Query<Employee>().ToListAsync(ct);
            var switched = 0;
            foreach (var employee in employees.Where(x => Employee.IsWorkRole(x.Role) && !x.AutoAcceptWork))
            {
                employee.AutoAcceptWork = true;
                session.Store(employee);
                switched++;
            }
            session.Store(new DataCorrectionRecord { Id = MarkerId(), Key = Key, AppliedUtc = DateTime.UtcNow, RemovedCount = 0, ReportedCount = switched });
            await session.SaveChangesAsync(ct);

            logger.LogInformation("Automatic acceptance default tenant {Tenant}: {Switched} work employees switched to automatic", tenant, switched);
            results.Add((tenant, switched));
        }
        return results;
    }

    private static Guid MarkerId() => new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("data-correction:" + Key)).AsSpan(0, 16));
}
