using System.Security.Claims;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Employee identity + self-service endpoints (my shifts, shift clock, quality-cycle form).</summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IEmployeeRepository _employeeRepository;

    public MeController(IMediator mediator, IEmployeeRepository employeeRepository)
    {
        _mediator = mediator;
        _employeeRepository = employeeRepository;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var realm = HttpContext.Items["Realm"] as string ?? "";
        var roles = User.FindAll("realm_access")
            .Select(c => c.Value)
            .ToList();

        return Ok(new { realm, roles, sub = User.FindFirstValue("sub") });
    }

    [HttpGet("shifts")]
    public async Task<IActionResult> GetMyShifts(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var employeeId = await ResolveEmployeeId(ct);
        var fromUtc = from ?? DateTime.UtcNow.Date;
        var toUtc = to ?? fromUtc.AddDays(7);

        var result = await _mediator.Send(new GetMyShiftsQuery(employeeId, fromUtc, toUtc), ct);
        return Ok(result);
    }

    [HttpPost("shifts/{shiftId:guid}/start")]
    public async Task<IActionResult> StartShift(Guid shiftId, [FromBody] StartShiftRequest request, CancellationToken ct)
    {
        var employeeId = await ResolveEmployeeId(ct);
        var result = await _mediator.Send(
            new StartQualityCycleFormCommand(shiftId, employeeId, request.OccurrenceStartUtc, request.OccurrenceEndUtc), ct);
        return Ok(result);
    }

    [HttpPost("quality-cycle/{formId:guid}/end")]
    public async Task<IActionResult> EndForm(Guid formId, [FromBody] EndFormRequest request, CancellationToken ct)
    {
        var employeeId = await ResolveEmployeeId(ct);
        var result = await _mediator.Send(
            new EndQualityCycleFormCommand(formId, employeeId, request.Items, request.PhotoUrls, request.CleanerNotes), ct);
        return Ok(result);
    }

    private async Task<Guid> ResolveEmployeeId(CancellationToken ct)
    {
        var sub = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub))
            throw new UnauthorizedAccessException();

        var employee = await _employeeRepository.GetByKeycloakUserIdAsync(sub, ct);
        if (employee == null)
            throw new UnauthorizedAccessException();

        if (employee.RegisteredAtUtc == null)
        {
            employee.RegisteredAtUtc = DateTime.UtcNow;
            employee.UpdatedUtc = DateTime.UtcNow;
            await _employeeRepository.SaveAsync(employee, ct);
        }

        return employee.Id;
    }
}

public record StartShiftRequest(DateTime OccurrenceStartUtc, DateTime OccurrenceEndUtc);

public record EndFormRequest(
    List<QualityCycleFormItemDto> Items,
    List<string>? PhotoUrls = null,
    string? CleanerNotes = null);
