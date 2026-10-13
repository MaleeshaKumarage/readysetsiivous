using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
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

    public QualityCycleDispatcher(IShiftRepository shifts, IQualityCycleRepository forms,
        IEmployeeRepository employees, TimeProvider? clock = null)
    {
        _shifts = shifts; _forms = forms; _employees = employees;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<int> EnsureUpcomingAsync(Guid? shiftId = null, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<Shift> shifts = shiftId.HasValue
            ? (await _shifts.GetAsync(shiftId.Value, ct) is { } selectedShift ? new[] { selectedShift } : Array.Empty<Shift>())
            : await _shifts.ListAsync(null, null, ct);
        var count = 0;
        foreach (var shift in shifts.Where(s => s.IsActive))
        {
            var occurrences = QualityCycleOccurrences.InWindow(shift, now.Date, now.Date.AddDays(32))
                .Where(x => x.Occurrence.EndUtc > now).Select(x => x.Occurrence).ToList();
            count += await DispatchAsync(shift, occurrences, ct);
        }
        return count;
    }

    public async Task<int> DispatchAsync(Shift shift, IReadOnlyList<ShiftOccurrence> occurrences, CancellationToken ct = default)
    {
        if (!shift.IsActive || !shift.QualityCycleTemplateId.HasValue) return 0;
        var template = await _forms.GetTemplateAsync(shift.QualityCycleTemplateId.Value, ct);
        if (template is null || !template.IsActive) return 0;
        if ((template.CompanyId.HasValue && template.CompanyId != shift.CompanyId)
            || (template.BranchId.HasValue && template.BranchId != shift.BranchId))
            throw new ShiftConflictException("The checklist template does not belong to this shift's company/branch.");
        var assignments = await _shifts.ListAssignmentsByShiftAsync(shift.Id, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var count = 0;
        foreach (var assignment in assignments.Where(a => a.IsActive))
        {
            var employee = await _employees.GetByIdAsync(assignment.EmployeeId, ct);
            if (employee is null || !employee.IsActive) continue;
            foreach (var occurrence in occurrences.Where(o => o.EndUtc > assignment.AssignedAtUtc))
            {
                ct.ThrowIfCancellationRequested();
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
