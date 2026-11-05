using System.Globalization;
using System.Security.Claims;
using CleaningSuite.Api.Auth;
using CleaningSuite.Application;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/admin/shifts/calendar")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "admin")]
public class ShiftCalendarAdminController(IShiftRepository shifts, IEmployeeRepository employees,
    IQualityCycleRepository forms, ICompanyRepository companies, IBranchRepository branches,
    ITenantProfileRepository profiles, ITenantContext tenant, IShiftWorkReservation reservation) : ControllerBase
{
    // Explain the refusal so the admin knows to ask for calendar access instead of seeing a blank 403.
    private ObjectResult CalendarForbidden() => StatusCode(StatusCodes.Status403Forbidden,
        new { detail = "Your account does not have shift calendar access. Ask an owner to enable Shift calendar for your employee record, then reload." });

    private async Task<bool> Allowed(CancellationToken ct)
    {
        var subject = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject) || !string.Equals(User.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase)) return false;
        return (await employees.GetByKeycloakUserIdAsync(subject, ct))?.CanUseShiftCalendar == true;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? date, CancellationToken ct)
    {
        if (!await Allowed(ct)) return CalendarForbidden();
        var (zoneId, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var today = TenantTimeZone.LocalDate(DateTime.UtcNow, zone);
        var day = today;
        if (date is not null && !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            return BadRequest(new { detail = "Choose a date in YYYY-MM-DD format." });
        if (day.Year < 1900 || day.Year > 9998) return BadRequest(new { detail = "Choose a supported date." });
        var from = TenantTimeZone.StartOfLocalDay(day, zone);
        var to = TenantTimeZone.StartOfLocalDay(day.AddDays(1), zone);
        var staff = (await employees.ListAsync(true, ct)).ToDictionary(e => e.Id);
        var work = await forms.ListFormsAsync(null, from, to, ct);
        var result = new List<object>();
        var companyNames = new Dictionary<Guid, string>(); var branchNames = new Dictionary<Guid, string>();
        foreach (var shift in (await shifts.ListAsync(null, null, ct)).Where(s => s.IsActive))
        {
            var visits = await shifts.ResolveRosterAsync(shift, from, to, zone, ct, includeCancelled: true);
            if (visits.Count == 0) continue;
            if (!companyNames.ContainsKey(shift.CompanyId)) companyNames[shift.CompanyId] = (await companies.GetAsync(shift.CompanyId, ct))?.Name ?? "Unknown company";
            if (!branchNames.ContainsKey(shift.BranchId)) branchNames[shift.BranchId] = (await branches.GetAsync(shift.BranchId, ct))?.Name ?? "Unknown branch";
            foreach (var visit in visits.OrderBy(v => v.Occurrence.StartUtc))
            {
                var occurrence = visit.Occurrence;
                var recorded = work.Where(f => f.ShiftId == shift.Id && f.ShiftOccurrenceUtc == occurrence.StartUtc && f.ShiftDeletedUtc is null).ToList();
                var ambiguous = visits.Count(v => v.Occurrence.StartUtc == occurrence.StartUtc) > 1;
                var assignees = visit.EmployeeIds.Concat(recorded.Select(f => f.EmployeeId)).Distinct().Select(id => {
                    staff.TryGetValue(id, out var person);
                    var form = recorded.FirstOrDefault(f => f.EmployeeId == id);
                    // Same boundary as correction: every review flag has an available action; today/future never flag.
                    var correctable = day < today && !visit.IsCancelled && !ambiguous && person?.IsActive == true && Employee.IsWorkRole(person.Role);
                    var lastCorrection = form?.WorkTimeCorrections is { Count: > 0 } corrections ? corrections[^1] : null;
                    var corrected = lastCorrection is not null && lastCorrection.StartUtc == form!.StartedAtUtc && lastCorrection.EndUtc == form.EndedAtUtc;
                    var flag = CompletionReview.Evaluate(correctable, occurrence.StartUtc, occurrence.EndUtc, form?.IsSubmitted == true,
                        form?.StartedAtUtc, form?.EndedAtUtc, corrected);
                    return new { employeeId = id, name = person is null ? form?.EmployeeName ?? "Unknown employee" : $"{person.FirstName} {person.LastName}",
                        actualStartUtc = form?.StartedAtUtc, actualEndUtc = form?.EndedAtUtc,
                        hours = form?.StartedAtUtc is DateTime start && form.EndedAtUtc is DateTime end ? (double?)(end - start).TotalHours : null,
                        status = form?.IsSubmitted == true ? "Recorded" : form?.StartedAtUtc is not null ? "Started" : "Not started",
                        version = form?.Version, canRecord = correctable,
                        review = flag is null ? (object?)null : new { kind = flag.Kind, label = flag.Label } };
                }).ToArray();
                result.Add(new { shiftId = shift.Id, shiftName = shift.Name, companyName = companyNames[shift.CompanyId], branchName = branchNames[shift.BranchId],
                    occurrence.StartUtc, occurrence.EndUtc, occurrence.ExceptionId, cancelled = visit.IsCancelled, ambiguous,
                    needsReview = assignees.Any(a => a.review is not null), assignees });
            }
        }
        return Ok(new { date = day.ToString("yyyy-MM-dd"), today = today.ToString("yyyy-MM-dd"), timeZoneId = zoneId, occurrences = result });
    }

    public record WorkTimeBody(Guid ShiftId, Guid EmployeeId, DateTime OccurrenceStartUtc, Guid? ExceptionId,
        string ActualStartLocal, string ActualEndLocal, string Reason, Guid? Version);

    // Local date/time strings are interpreted by the server, never the browser's time zone.
    public static DateTime ParseLocal(string value, TimeZoneInfo zone)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            || zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ArgumentException("Choose an unambiguous valid local date and time (YYYY-MM-DDTHH:mm).");
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    }

    [HttpPut("work-time")]
    public async Task<IActionResult> Record(WorkTimeBody body, CancellationToken ct)
    {
        if (!await Allowed(ct)) return CalendarForbidden();
        await using var lease = await reservation.AcquireAsync(ct);
        // Recheck after acquiring the reservation, including revocation/lifecycle changes.
        if (!await Allowed(ct)) return CalendarForbidden();
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var now = DateTime.UtcNow;
        var scheduled = DateTime.SpecifyKind(body.OccurrenceStartUtc, DateTimeKind.Utc);
        if (scheduled.Year < 1900 || scheduled.Year > 9998) return BadRequest(new { detail = "Choose a supported visit date." });
        var day = TenantTimeZone.LocalDate(scheduled, zone);
        if (day >= TenantTimeZone.LocalDate(now, zone)) return BadRequest(new { detail = "Only past dates can be recorded." });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length > 2000) return BadRequest(new { detail = "Enter a reason (up to 2000 characters)." });
        DateTime actualStart, actualEnd;
        try { actualStart = ParseLocal(body.ActualStartLocal, zone); actualEnd = ParseLocal(body.ActualEndLocal, zone); }
        catch (ArgumentException error) { return BadRequest(new { detail = error.Message }); }
        if (actualEnd <= actualStart || actualEnd > now || actualStart > now) return BadRequest(new { detail = "End must be after start and neither time can be in the future." });
        var shift = await shifts.GetAsync(body.ShiftId, ct);
        var employee = await employees.GetByIdAsync(body.EmployeeId, ct);
        if (shift?.IsActive != true || employee?.IsActive != true) return BadRequest(new { detail = "Choose an active shift and employee." });
        if (!Employee.IsWorkRole(employee.Role)) return BadRequest(new { detail = "Choose an employee with a work role." });
        var visits = await shifts.ResolveRosterAsync(shift, TenantTimeZone.StartOfLocalDay(day, zone), TenantTimeZone.StartOfLocalDay(day.AddDays(1), zone), zone, ct, includeCancelled: true);
        var matching = visits.Where(v => v.Occurrence.StartUtc == scheduled).ToList();
        if (matching.Count != 1 || matching[0].Occurrence.ExceptionId != body.ExceptionId || matching[0].IsCancelled)
            return Conflict(new { detail = "This visit changed or has an ambiguous work-record identity. Reload the calendar." });
        var visit = matching[0];
        var form = await forms.GetFormByShiftOccurrenceAsync(shift.Id, employee.Id, scheduled, ct);
        if (form?.ShiftDeletedUtc is not null || (!visit.EmployeeIds.Contains(employee.Id) && form is null))
            return Conflict(new { detail = "This visit was removed or no longer includes this employee. Reload the calendar before saving." });
        if (form?.Version != body.Version) return Conflict(new { detail = "Work times changed. Reload the calendar before saving." });
        var isNew = form is null;
        if (form is null)
        {
            var template = shift.QualityCycleTemplateId is Guid templateId ? await forms.GetTemplateAsync(templateId, ct) : null;
            if (template is not null && (!template.IsActive || (template.CompanyId.HasValue && template.CompanyId != shift.CompanyId) || (template.BranchId.HasValue && template.BranchId != shift.BranchId)))
                return Conflict(new { detail = "The shift checklist changed. Correct its template before recording work." });
            form = QualityCycleForm.Create(shift.Id, shift.Name, employee.Id, $"{employee.FirstName} {employee.LastName}",
                template?.Id ?? Guid.Empty, template?.Title ?? "Shift activities", scheduled, template?.Items ?? new(), visit.Occurrence.EndUtc);
            form.Id = QualityCycleDispatcher.TaskId(shift.Id, employee.Id, scheduled);
            form.CompanyId = shift.CompanyId; form.BranchId = shift.BranchId;
        }
        form.WorkTimeCorrections.Add(new(User.FindFirstValue("sub")!, now, body.Reason.Trim(), form.StartedAtUtc, form.EndedAtUtc, actualStart, actualEnd));
        // This operation explicitly records BOTH start and end, completing a past
        // visit even when the cleaner previously started it. It is not a start-only
        // timer correction. Retain all existing checklist answers and evidence.
        form.StartedAtUtc = actualStart; form.EndedAtUtc = actualEnd; form.IsSubmitted = true;
        form.SubmittedUtc ??= now; form.UpdatedUtc = now;
        if (isNew) await forms.CreateStartedFormAsync(form, ct); else await forms.SaveFormAsync(form, ct);
        return Ok(new { form.Id, form.Version, actualStartUtc = actualStart, actualEndUtc = actualEnd });
    }
}
