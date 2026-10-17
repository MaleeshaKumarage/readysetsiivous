using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.QualityCycle;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class QualityCycleRepository : IQualityCycleRepository
{
    private readonly ITenantSession _tenant;
    private readonly IDocumentStore _store;
    private IDocumentSession _session => _tenant.Session;

    public QualityCycleRepository(ITenantSession tenant, IDocumentStore store)
    {
        _tenant = tenant;
        _store = store;
    }

    public async Task<QualityCycleTemplate?> GetTemplateAsync(Guid id, CancellationToken ct = default)
    {
        return await _session.LoadAsync<QualityCycleTemplate>(id, ct);
    }

    public async Task<IReadOnlyList<QualityCycleTemplate>> ListTemplatesAsync(Guid? companyId, Guid? branchId, CancellationToken ct = default)
    {
        var query = _session.Query<QualityCycleTemplate>().AsQueryable();

        if (companyId.HasValue && companyId.Value != Guid.Empty)
        {
            query = query.Where(x => x.CompanyId == companyId.Value || x.CompanyId == null);
        }

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(x => x.BranchId == branchId.Value || x.BranchId == null);
        }

        return await query.ToListAsync(ct);
    }

    public async Task SaveTemplateAsync(QualityCycleTemplate template, CancellationToken ct = default)
    {
        _session.Store(template);
        await _session.SaveChangesAsync(ct);
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        _session.Delete<QualityCycleTemplate>(id);
        await _session.SaveChangesAsync(ct);
    }

    public async Task<QualityCycleForm?> GetFormAsync(Guid id, CancellationToken ct = default)
    {
        return await _session.LoadAsync<QualityCycleForm>(id, ct);
    }

    public async Task<QualityCycleForm?> GetFormByTokenAsync(string token, CancellationToken ct = default)
    {
        return await _session.Query<QualityCycleForm>()
            .FirstOrDefaultAsync(x => x.Token == token, ct);
    }

    public async Task<QualityCycleForm?> GetFormByShiftOccurrenceAsync(Guid shiftId, Guid employeeId, DateTime shiftOccurrenceUtc, CancellationToken ct = default)
    {
        var start = DateTime.SpecifyKind(shiftOccurrenceUtc, DateTimeKind.Unspecified);
        return await _session.Query<QualityCycleForm>()
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId && x.EmployeeId == employeeId && x.ShiftOccurrenceUtc == start, ct);
    }

    public Task<IReadOnlyList<QualityCycleForm>> ListFormsAsync(Guid? shiftId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default) =>
        ListFormsAsync(shiftId, fromUtc, toUtc, ct, null, null, null, null);

    public async Task<IReadOnlyList<QualityCycleForm>> ListFormsAsync(Guid? shiftId, DateTime? fromUtc, DateTime? toUtc,
        CancellationToken ct, Guid? employeeId, Guid? templateId, Guid? companyId, Guid? branchId)
    {
        var query = _session.Query<QualityCycleForm>().AsQueryable();

        if (shiftId.HasValue && shiftId.Value != Guid.Empty)
        {
            query = query.Where(x => x.ShiftId == shiftId.Value);
        }
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        if (templateId.HasValue) query = query.Where(x => x.TemplateId == templateId.Value);
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId.Value);

        if (fromUtc.HasValue)
        {
            var from = DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Unspecified);
            query = query.Where(x => x.ShiftOccurrenceUtc >= from);
        }

        if (toUtc.HasValue)
        {
            var to = DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Unspecified);
            query = query.Where(x => x.ShiftOccurrenceUtc < to);
        }

        return await query.OrderByDescending(x => x.ShiftOccurrenceUtc).ToListAsync(ct);
    }

    public async Task SaveFormAsync(QualityCycleForm form, CancellationToken ct = default)
    {
        _session.Store(form);
        await _session.SaveChangesAsync(ct);
    }

    public async Task CreateStartedFormAsync(QualityCycleForm form, CancellationToken ct = default)
    {
        // Clock-start authorisation and the started record use the same reservation session.
        _session.Insert(form);
        try { await _session.SaveChangesAsync(ct); }
        catch (Exception error) when (IsDuplicate(error))
        { throw new QualityCycleConflictException("The shift task changed. Reload your shifts and retry."); }
    }

    public async Task<bool> TryCreateFormAsync(QualityCycleForm form, CancellationToken ct = default)
    {
        // A separate insert transaction prevents a competing generator from overwriting
        // a cleaner's existing task or poisoning the request's tracked session.
        await using var session = _store.LightweightSession(_tenant.TenantId);
        try
        {
            session.Insert(form);
            await session.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (IsDuplicate(ex)) { return false; }
    }

    public async Task SaveReportShareAsync(QualityCycleReportShare share, CancellationToken ct = default) {
        _session.Store(share); await _session.SaveChangesAsync(ct);
    }
    public Task<QualityCycleReportShare?> GetReportShareAsync(Guid id, CancellationToken ct = default) => _session.LoadAsync<QualityCycleReportShare>(id, ct);
    public Task<QualityCycleReportShare?> GetReportShareByTokenAsync(string token, CancellationToken ct = default) =>
        _session.Query<QualityCycleReportShare>().FirstOrDefaultAsync(s => s.Token == token, ct);
    public async Task<IReadOnlyList<QualityCycleReportShare>> ListReportSharesAsync(CancellationToken ct = default) =>
        await _session.Query<QualityCycleReportShare>().OrderByDescending(s => s.CreatedUtc).ToListAsync(ct);

    private static bool IsDuplicate(Exception ex) =>
        ex is JasperFx.DocumentAlreadyExistsException
        || ex is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation }
        || (ex.InnerException is { } inner && IsDuplicate(inner));
}
