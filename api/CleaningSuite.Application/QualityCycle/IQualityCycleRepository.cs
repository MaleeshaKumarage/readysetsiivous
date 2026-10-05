using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Application.QualityCycle;

public interface IQualityCycleRepository
{
    // Templates
    Task<QualityCycleTemplate?> GetTemplateAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<QualityCycleTemplate>> ListTemplatesAsync(Guid? companyId, Guid? branchId, CancellationToken ct = default);
    Task SaveTemplateAsync(QualityCycleTemplate template, CancellationToken ct = default);
    Task DeleteTemplateAsync(Guid id, CancellationToken ct = default);

    // Forms
    Task<QualityCycleForm?> GetFormAsync(Guid id, CancellationToken ct = default);
    Task<QualityCycleForm?> GetFormByTokenAsync(string token, CancellationToken ct = default);
    Task<QualityCycleForm?> GetFormByShiftOccurrenceAsync(Guid shiftId, Guid employeeId, DateTime shiftOccurrenceUtc, CancellationToken ct = default);
    Task<IReadOnlyList<QualityCycleForm>> ListFormsAsync(Guid? shiftId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
    Task SaveFormAsync(QualityCycleForm form, CancellationToken ct = default);
}
