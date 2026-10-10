using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.QualityCycle;

public class QualityCycleDispatcher : IQualityCycleDispatcher
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IQualityCycleRepository _qcRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmailSender _emailSender;

    public QualityCycleDispatcher(
        IShiftRepository shiftRepository,
        IQualityCycleRepository qcRepository,
        IEmployeeRepository employeeRepository,
        IEmailSender emailSender)
    {
        _shiftRepository = shiftRepository;
        _qcRepository = qcRepository;
        _employeeRepository = employeeRepository;
        _emailSender = emailSender;
    }

    public async Task<int> DispatchAsync(Shift shift, IReadOnlyList<ShiftOccurrence> occurrences, CancellationToken ct = default)
    {
        if (!shift.QualityCycleTemplateId.HasValue || occurrences.Count == 0) return 0;

        var template = await _qcRepository.GetTemplateAsync(shift.QualityCycleTemplateId.Value, ct);
        if (template == null || !template.IsActive) return 0;

        var assignments = await _shiftRepository.ListAssignmentsByShiftAsync(shift.Id, ct);
        var employees = new List<Employee>();
        foreach (var assignment in assignments)
        {
            var emp = await _employeeRepository.GetByIdAsync(assignment.EmployeeId, ct);
            if (emp != null && emp.IsActive)
            {
                employees.Add(emp);
            }
        }

        int dispatched = 0;
        foreach (var occurrence in occurrences)
        {
            foreach (var employee in employees)
            {
                var existing = await _qcRepository.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, occurrence.StartUtc, ct);
                if (existing != null) continue;

                var form = QualityCycleForm.Create(
                    shift.Id,
                    shift.Name,
                    employee.Id,
                    $"{employee.FirstName} {employee.LastName}",
                    template.Id,
                    template.Title,
                    occurrence.StartUtc,
                    template.Items,
                    occurrence.EndUtc);

                await _qcRepository.SaveFormAsync(form, ct);
                dispatched++;

                if (!string.IsNullOrWhiteSpace(employee.Email))
                {
                    var formUrl = $"https://readysetsiivous.fi/quality-cycle?token={form.Token}";
                    var subject = $"Quality Cycle Checklist: {shift.Name} ({occurrence.StartUtc:yyyy-MM-dd})";
                    var body = $@"Hello {employee.FirstName},

Please complete the Quality Cycle form for your shift '{shift.Name}' on {occurrence.StartUtc:yyyy-MM-dd HH:mm UTC}.

Form Link: {formUrl}

Thank you,
ReadySetSiivous Team";

                    try
                    {
                        await _emailSender.SendAsync(employee.Email, subject, body, ct);
                    }
                    catch
                    {
                        // Email failures must not roll back the form.
                    }
                }
            }
        }

        return dispatched;
    }
}
