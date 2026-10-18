using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Application.QualityCycle;

public interface IQualityCycleRepository
{
    // Templates
    Task<QualityCycleTemplate?> GetTemplateAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<QualityCycleTemplate>> ListTemplatesAsync(Guid? companyId, Guid? branchId, CancellationToken ct = default);
    Task SaveTemplateAsync(QualityCycleTemplate template, CancellationToken ct = default);
    Task DeleteTemplateAsync(Guid id, CancellationToken ct = default);

    Task SaveReportShareAsync(QualityCycleReportShare share, CancellationToken ct = default);
    Task<QualityCycleReportShare?> GetReportShareAsync(Guid id, CancellationToken ct = default);
    Task<QualityCycleReportShare?> GetReportShareByTokenAsync(string token, CancellationToken ct = default);
    Task<IReadOnlyList<QualityCycleReportShare>> ListReportSharesAsync(CancellationToken ct = default);
    Task AcknowledgeReportShareAsync(string token, DateTime now, CancellationToken ct = default);
    Task UpdateReportShareDeliveryAsync(Guid id, DateTime? sentUtc, string? error, CancellationToken ct = default);
    Task RevokeReportShareAsync(Guid id, CancellationToken ct = default);

    // Forms
    Task<QualityCycleForm?> GetFormAsync(Guid id, CancellationToken ct = default);
    Task<QualityCycleForm?> GetFormByTokenAsync(string token, CancellationToken ct = default);
    Task<QualityCycleForm?> GetFormByShiftOccurrenceAsync(Guid shiftId, Guid employeeId, DateTime shiftOccurrenceUtc, CancellationToken ct = default);
    Task<IReadOnlyList<QualityCycleForm>> ListFormsAsync(Guid? shiftId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<QualityCycleForm>> ListFormsAsync(Guid? shiftId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct,
        Guid? employeeId, Guid? templateId, Guid? companyId, Guid? branchId);
    Task SaveFormAsync(QualityCycleForm form, CancellationToken ct = default);
    Task<bool> TryCreateFormAsync(QualityCycleForm form, CancellationToken ct = default);
    Task CreateStartedFormAsync(QualityCycleForm form, CancellationToken ct = default);
}
