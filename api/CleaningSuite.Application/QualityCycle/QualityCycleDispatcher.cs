using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.QualityCycle;

/// <summary>Shared by assignment, template changes, recovery and the periodic worker.</summary>
public class QualityCycleDispatcher : IQualityCycleDispatcher
{
    private readonly IShiftRepository _shifts;
    private readonly IQualityCycleRepository _forms;
    private readonly IEmployeeRepository _employees;
    private readonly TimeProvider _clock;
    private readonly ITenantProfileRepository? _profiles;
    private readonly ITenantContext? _tenant;

    public QualityCycleDispatcher(IShiftRepository shifts, IQualityCycleRepository forms,
        IEmployeeRepository employees, TimeProvider? clock = null,
        ITenantProfileRepository? profiles = null, ITenantContext? tenant = null)
    {
        _shifts = shifts; _forms = forms; _employees = employees;
        _clock = clock ?? TimeProvider.System;
        _profiles = profiles; _tenant = tenant;
    }

    public async Task<int> EnsureUpcomingAsync(Guid? shiftId = null, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var (_, zone) = await TenantTimeZone.LoadAsync(_profiles, _tenant, ct);
        var localToday = TenantTimeZone.LocalDate(now, zone);
        var fromUtc = TenantTimeZone.StartOfLocalDay(localToday, zone);
        var toUtc = TenantTimeZone.StartOfLocalDay(localToday.AddDays(32), zone);
        IReadOnlyList<Shift> shifts = shiftId.HasValue
            ? (await _shifts.GetAsync(shiftId.Value, ct) is { } selectedShift ? new[] { selectedShift } : Array.Empty<Shift>())
            : await _shifts.ListAsync(null, null, ct);
        var count = 0;
        foreach (var shift in shifts.Where(s => s.IsActive))
        {
            var generated = QualityCycleOccurrences.InWindow(shift, fromUtc, toUtc, zone)
                .Select(x => x.Occurrence).ToList();
            var exceptions = await _shifts.ListOccurrenceExceptionsAsync(shift.Id, fromUtc, toUtc, ct)
                ?? Array.Empty<ShiftOccurrenceException>();
            var occurrences = ShiftOccurrenceResolver.Apply(generated, exceptions)
                .Where(x => x.EndUtc > now).ToList();
            count += await DispatchResolvedAsync(shift, occurrences, ct);
        }
        return count;
    }

    public async Task<int> DispatchAsync(Shift shift, IReadOnlyList<ShiftOccurrence> occurrences, CancellationToken ct = default)
    {
        if (occurrences.Count == 0) return 0;
        var from = occurrences.Min(x => x.StartUtc); var to = occurrences.Max(x => x.EndUtc);
        var exceptions = await _shifts.ListOccurrenceExceptionsAsync(shift.Id, from, to, ct) ?? [];
        return await DispatchResolvedAsync(shift, ShiftOccurrenceResolver.Apply(occurrences, exceptions)
            .Where(x => x.StartUtc >= from && x.StartUtc < to).ToList(), ct);
    }

    private async Task<int> DispatchResolvedAsync(Shift shift, IReadOnlyList<ResolvedShiftOccurrence> occurrences, CancellationToken ct)
    {
        if (!shift.IsActive || !shift.QualityCycleTemplateId.HasValue || occurrences.Count == 0) return 0;
        var template = await _forms.GetTemplateAsync(shift.QualityCycleTemplateId.Value, ct);
        if (template is null || !template.IsActive) return 0;
        if ((template.CompanyId.HasValue && template.CompanyId != shift.CompanyId)
            || (template.BranchId.HasValue && template.BranchId != shift.BranchId))
            throw new ShiftConflictException("The checklist template does not belong to this shift's company/branch.");
        var assignments = await _shifts.ListAssignmentsByShiftAsync(shift.Id, ct) ?? [];
        var history = await _shifts.ListAssignmentHistoryAsync(shift.Id, ct) ?? [];
        var overrides = await _shifts.ListOccurrenceAssignmentsAsync(shift.Id, ct) ?? [];
        var recurring = history.Concat(assignments).GroupBy(x => (x.Id, x.EmployeeId)).Select(x => x.First()).ToList();
        var now = _clock.GetUtcNow().UtcDateTime;
        var (_, zone) = await TenantTimeZone.LoadAsync(_profiles, _tenant, ct);
        var count = 0;
        foreach (var occurrence in occurrences)
        {
            var original = occurrence.Status == ShiftOccurrenceExceptionStatus.Extra ? (DateTime?)null
                : occurrence.OriginalStartUtc ?? occurrence.StartUtc;
            var change = overrides.FirstOrDefault(x => x.Id == ShiftOccurrenceAssignment.Identity(shift.Id, original,
                occurrence.Status == ShiftOccurrenceExceptionStatus.Extra ? occurrence.ExceptionId : null));
            var regular = recurring.Where(a => a.CoversVisit(occurrence.StartUtc, occurrence.EndUtc, shift.ValidFrom.HasValue ? TenantTimeZone.StartOfLocalDay(shift.ValidFrom.Value.Date, zone) : null)).Select(a => a.EmployeeId);
            var employeeIds = (change is not null ? change.Mode switch {
                OccurrenceAssignmentMode.Replace => change.EmployeeIds,
                OccurrenceAssignmentMode.Add => regular.Concat(change.EmployeeIds),
                _ => regular
            } : occurrence.EmployeeId.HasValue ? new[] { occurrence.EmployeeId.Value } : regular).Distinct();
            foreach (var employeeId in employeeIds)
            {
                ct.ThrowIfCancellationRequested();
                var employee = await _employees.GetByIdAsync(employeeId, ct);
                if (employee is null || !employee.IsActive) continue;
                var form = await _forms.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, occurrence.StartUtc, ct);
                if (form is null)
                {
                    form = QualityCycleForm.Create(shift.Id, shift.Name, employee.Id,
                        $"{employee.FirstName} {employee.LastName}", template.Id, template.Title,
                        occurrence.StartUtc, template.Items, occurrence.EndUtc);
                    form.Id = TaskId(shift.Id, employee.Id, occurrence.StartUtc);
                    form.CompanyId = shift.CompanyId; form.BranchId = shift.BranchId;
                    if (!await _forms.TryCreateFormAsync(form, ct)) continue;
                    count++;
                }
                else if (!form.IsSubmitted && !form.StartedAtUtc.HasValue && form.ShiftOccurrenceUtc > now
                    && (form.TemplateId != template.Id || !form.Items.Select(i => i.ItemText).SequenceEqual(template.Items)
                        || form.ShiftOccurrenceEndUtc != occurrence.EndUtc || form.ShiftName != shift.Name
                        || form.CompanyId != shift.CompanyId || form.BranchId != shift.BranchId))
                {
                    // Future tasks follow the template; started/completed tasks retain their snapshot.
                    form.TemplateId = template.Id; form.TemplateTitle = template.Title;
                    form.Items = template.Items.Select(i => new QualityCycleFormItem { ItemText = i }).ToList();
                    form.CompanyId = shift.CompanyId; form.BranchId = shift.BranchId;
                    form.ShiftName = shift.Name; form.ShiftOccurrenceEndUtc = occurrence.EndUtc;
                    await _forms.SaveFormAsync(form, ct);
                }
            }
        }
        return count;
    }

    public static Guid TaskId(Guid shiftId, Guid employeeId, DateTime startUtc)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"qc:{shiftId:N}:{employeeId:N}:{startUtc.Ticks}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
