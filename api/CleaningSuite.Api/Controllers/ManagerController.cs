using System.Text.Json.Serialization;
using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

[ApiController]
[Route("api/v1/manager")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "operations")]
public sealed class ManagerController(ManagerOperations operations) : ControllerBase
{
    [HttpGet("employees")] public async Task<IActionResult> Employees(CancellationToken ct) => Ok(await operations.Roster(ct));
    [HttpGet("companies")] public async Task<IActionResult> Companies([FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default) => Ok(await operations.Companies(skip, take, ct));
    [HttpGet("companies/{id:guid}/branches")] public async Task<IActionResult> Branches(Guid id, CancellationToken ct) => Ok(await operations.Branches(id, ct));
    [HttpGet("shifts")] public async Task<IActionResult> Shifts(CancellationToken ct, [FromQuery] bool includeInactive = false) => Ok(await operations.List(ct, includeInactive));
    [HttpGet("time-zone")] public async Task<IActionResult> TimeZone(CancellationToken ct) => Ok(new { timeZoneId = await operations.TimeZone(ct) });
    [HttpGet("shifts/{id:guid}")] public async Task<IActionResult> Shift(Guid id, CancellationToken ct) => Ok(await operations.Get(id, ct));
    [HttpPost("shifts")] public async Task<IActionResult> Create(CreateManagerShiftRequest body, CancellationToken ct) => Ok(await operations.Create(body.CompanyId, body.BranchId, body.Name, body.Schedule, body.ValidFrom, body.ValidUntil, ct));
    [HttpPut("shifts/{id:guid}")] public async Task<IActionResult> Update(Guid id, UpdateManagerShiftRequest body, CancellationToken ct) => Ok(await operations.Update(id, body.Name, body.Schedule, body.IsActive, body.ValidFrom, body.ValidUntil, ct));
    [HttpPost("shifts/{id:guid}/deactivate")] public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) { await operations.Deactivate(id, ct); return NoContent(); }
    [HttpPost("shifts/{id:guid}/reactivate")] public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct) => Ok(await operations.Reactivate(id, ct));
    [HttpDelete("shifts/{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await operations.Delete(id, ct); return NoContent(); }
    [HttpGet("shifts/{id:guid}/assignments")] public async Task<IActionResult> Assignments(Guid id, CancellationToken ct) => Ok(await operations.Assignments(id, ct));
    [HttpPost("shifts/{id:guid}/assignments")] public async Task<IActionResult> Assign(Guid id, ManagerAssignmentRequest body, CancellationToken ct) => Ok(await operations.Assign(id, body.EmployeeId, ct));
    [HttpDelete("shifts/{id:guid}/assignments/{employeeId:guid}")] public async Task<IActionResult> Unassign(Guid id, Guid employeeId, CancellationToken ct) { await operations.Unassign(id, employeeId, ct); return NoContent(); }
    [HttpGet("shifts/{id:guid}/occurrences")] public async Task<IActionResult> Occurrences(Guid id, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct) => Ok(await operations.Occurrences(id, from, to, ct));
    [HttpPut("shifts/{id:guid}/occurrence-exceptions")] public async Task<IActionResult> SaveOccurrence(Guid id, SaveShiftOccurrenceExceptionCommand body, CancellationToken ct) => Ok(await operations.SaveOccurrence(id, body, ct));
    [HttpDelete("shifts/{id:guid}/occurrence-exceptions/{exceptionId:guid}")] public async Task<IActionResult> DeleteOccurrence(Guid id, Guid exceptionId, CancellationToken ct) { await operations.DeleteOccurrence(id, exceptionId, ct); return NoContent(); }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record CreateManagerShiftRequest(Guid CompanyId, Guid BranchId, string Name, ShiftSchedule Schedule, DateTime? ValidFrom = null, DateTime? ValidUntil = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record UpdateManagerShiftRequest(string Name, ShiftSchedule Schedule, bool IsActive = true, DateTime? ValidFrom = null, DateTime? ValidUntil = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ManagerAssignmentRequest(Guid EmployeeId);
