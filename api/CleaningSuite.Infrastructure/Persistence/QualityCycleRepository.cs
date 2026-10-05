using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.QualityCycle;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class QualityCycleRepository : IQualityCycleRepository
{
    private readonly IDocumentSession _session;

    public QualityCycleRepository(IDocumentSession session)
    {
        _session = session;
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
        return await _session.Query<QualityCycleForm>()
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId && x.EmployeeId == employeeId && x.ShiftOccurrenceUtc == shiftOccurrenceUtc, ct);
    }

    public async Task<IReadOnlyList<QualityCycleForm>> ListFormsAsync(Guid? shiftId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var query = _session.Query<QualityCycleForm>().AsQueryable();

        if (shiftId.HasValue && shiftId.Value != Guid.Empty)
        {
            query = query.Where(x => x.ShiftId == shiftId.Value);
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.ShiftOccurrenceUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.ShiftOccurrenceUtc <= toUtc.Value);
        }

        return await query.OrderByDescending(x => x.ShiftOccurrenceUtc).ToListAsync(ct);
    }

    public async Task SaveFormAsync(QualityCycleForm form, CancellationToken ct = default)
    {
        _session.Store(form);
        await _session.SaveChangesAsync(ct);
    }
}
