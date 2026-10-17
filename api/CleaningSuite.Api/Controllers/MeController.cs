using System.Security.Claims;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Common;
using CleaningSuite.Api.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Employee identity + self-service endpoints (my shifts, shift clock, quality-cycle form).</summary>
[ApiController]
[Route("api/v1/me")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "staff")]
public class MeController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantProfileRepository _profiles;
    private readonly ITenantContext _tenant;
    public MeController(IMediator mediator, ITenantProfileRepository profiles, ITenantContext tenant)
    {
        _mediator = mediator;
        _profiles = profiles;
        _tenant = tenant;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var realm = HttpContext.Items["Realm"] as string ?? "";
        var roles = User.FindAll("realm_access")
            .Select(c => c.Value)
            .ToList();

        var employee = HttpContext.Items["Employee"] as Employee;
        return Ok(new { realm, roles, sub = User.FindFirstValue("sub"), employee = employee is null ? null : new { employee.Id, employee.FirstName, employee.LastName, employee.Role, employee.IsActive } });
    }

    [HttpGet("profile")]
    public IActionResult Profile() => Ok(SelfResponses.Employee(VerifiedEmployee()));

    [HttpGet("work")]
    public async Task<IActionResult> Work([FromQuery] string date, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetOwnWorkQuery(VerifiedEmployee().Id, date), ct));

    [HttpGet("work/bookings/{id:guid}")]
    public async Task<IActionResult> WorkBooking(Guid id, CancellationToken ct)
    {
        var booking = await _mediator.Send(new GetOwnBookingQuery(VerifiedEmployee().Id, id), ct);
        return booking is null ? NotFound() : Ok(booking);
    }

    [HttpGet("shifts")]
    public async Task<IActionResult> GetMyShifts(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var employeeId = VerifiedEmployee().Id;
        var (_, zone) = await TenantTimeZone.LoadAsync(_profiles, _tenant, ct);
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        var fromUtc = from ?? TenantTimeZone.StartOfLocalDay(localToday, zone);
        if (!to.HasValue && fromUtc > DateTime.MaxValue.AddDays(-7))
            throw new FluentValidation.ValidationException("Choose a supported shift date range.");
        var toUtc = to ?? TenantTimeZone.StartOfLocalDay(localToday.AddDays(7), zone);

        var result = await _mediator.Send(new GetMyShiftsQuery(employeeId, fromUtc, toUtc), ct);
        return Ok(result);
    }

    [HttpGet("shifts/calendar")]
    public async Task<IActionResult> ShiftCalendar([FromQuery] string view = "today", [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var employee = VerifiedEmployee();
        var (zoneId, zone) = await TenantTimeZone.LoadAsync(_profiles, _tenant, ct);
        var window = ShiftCalendarWindow.Create(view, cursor, DateTime.UtcNow, zone);
        var occurrences = await _mediator.Send(new GetMyShiftsQuery(employee.Id, window.FromUtc, window.ToUtc), ct);
        return Ok(new { occurrences, timeZoneId = zoneId, window.FromLocalDate, window.ToLocalDate });
    }

    [HttpPost("shifts/{shiftId:guid}/start")]
    public async Task<IActionResult> StartShift(Guid shiftId, [FromBody] StartShiftRequest request, CancellationToken ct)
    {
        var employeeId = VerifiedEmployee().Id;
        var result = await _mediator.Send(
            new StartQualityCycleFormCommand(shiftId, employeeId, request.OccurrenceStartUtc, request.OccurrenceEndUtc), ct);
        return Ok(result);
    }

    [HttpPost("quality-cycle/{formId:guid}/end")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> EndForm(Guid formId, [FromBody] EndFormRequest request, CancellationToken ct)
    {
        var employeeId = VerifiedEmployee().Id;
        var result = await _mediator.Send(
            new EndQualityCycleFormCommand(formId, employeeId, request.Items, request.PhotoUrls, request.CleanerNotes, request.Photos), ct);
        return Ok(result);
    }

    private Employee VerifiedEmployee()
    {
        // The middleware already verified current binding, lifecycle and claims.
        // Unbound tenant owners keep administration but have no fabricated self identity.
        return HttpContext.Items["Employee"] as Employee
            ?? throw new NotFoundException("Employee", Guid.Empty);
    }
}

public record StartShiftRequest(DateTime OccurrenceStartUtc, DateTime OccurrenceEndUtc);

public record EndFormRequest(
    List<QualityCycleFormItemDto> Items,
    List<string>? PhotoUrls = null,
    string? CleanerNotes = null,
    List<CleaningSuite.Domain.QualityCycle.QualityCyclePhoto>? Photos = null);
