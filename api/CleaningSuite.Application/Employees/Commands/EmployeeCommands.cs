using CleaningSuite.Application.Common;
using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Employees;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Application.Employees.Commands;

public record CertificationInput(string Name, DateTime? ExpiresAtUtc);

public record EmployeeFields(
    string Email,
    string FirstName,
    string LastName,
    string Phone,
    string Role,
    string? ColorHex,
    Dictionary<string, WorkHours> DefaultHours,
    List<string>? Skills,
    List<string>? ServiceAreas,
    decimal? PayRate,
    List<CertificationInput>? Certifications,
    string? Notes);

public record CreateEmployeeCommand(EmployeeFields Fields) : IRequest<Guid>;

public record UpdateEmployeeCommand(Guid Id, EmployeeFields Fields, bool IsActive) : IRequest<Unit>;

public record DeactivateEmployeeCommand(Guid Id) : IRequest<Unit>;

public record ReactivateEmployeeCommand(Guid Id) : IRequest<Unit>;
public record DeleteEmployeeCommand(Guid Id) : IRequest<Unit>;

public record InviteEmployeeCommand(Guid Id) : IRequest<EmployeeInviteResult>;

public record EmployeeInviteResult(string Email, string KeycloakUserId, string Status, DateTime? SentUtc);

public record AssignBookingCommand(Guid BookingId, Guid EmployeeId) : IRequest<Unit>;

public record UnassignBookingCommand(Guid BookingId) : IRequest<Unit>;

public class EmployeeFieldsValidator : AbstractValidator<EmployeeFields>
{
    public EmployeeFieldsValidator()
    {
        RuleFor(x => x.Email).EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).Must(r => r is Employee.RoleAdmin or Employee.RoleEmployee or Employee.RoleManager);
        RuleFor(x => x.PayRate).GreaterThanOrEqualTo(0).When(x => x.PayRate.HasValue);
        RuleForEach(x => x.Certifications).ChildRules(c =>
        {
            c.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        });
    }
}

public class CreateEmployeeValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeValidator() => RuleFor(x => x.Fields).SetValidator(new EmployeeFieldsValidator());
}

public class UpdateEmployeeValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeValidator() => RuleFor(x => x.Fields).SetValidator(new EmployeeFieldsValidator());
}

public class CreateEmployeeHandler : IRequestHandler<CreateEmployeeCommand, Guid>
{
    private readonly IEmployeeRepository _employees;

    public CreateEmployeeHandler(IEmployeeRepository employees) => _employees = employees;

    public async Task<Guid> Handle(CreateEmployeeCommand request, CancellationToken ct)
    {
        if (await _employees.GetByEmailAsync(request.Fields.Email.Trim(), ct) is { } existing)
            throw DuplicateEmail(existing);

        var employee = new Employee();
        Apply(employee, request.Fields);
        await _employees.SaveAsync(employee, ct);
        return employee.Id;
    }

    internal static EmployeeIdentityConflictException DuplicateEmail(Employee existing) => new(existing.IsActive
        ? "An active employee already uses this email. Use the existing employee profile instead of creating another account."
        : "An inactive employee already uses this email. Open the Inactive tab and choose Reactivate to restore the existing profile instead of creating another account.");

    internal static void Apply(Employee employee, EmployeeFields fields)
    {
        employee.Email = fields.Email.Trim();
        employee.FirstName = fields.FirstName;
        employee.LastName = fields.LastName;
        employee.Phone = fields.Phone;
        employee.Role = fields.Role;
        employee.ColorHex = fields.ColorHex;
        employee.DefaultHours = fields.DefaultHours;
        employee.Skills = fields.Skills ?? new List<string>();
        employee.ServiceAreas = fields.ServiceAreas ?? new List<string>();
        employee.PayRate = fields.PayRate;
        employee.Certifications = fields.Certifications?.Select(c => new Certification { Name = c.Name, ExpiresAtUtc = c.ExpiresAtUtc }).ToList() ?? new List<Certification>();
        employee.Notes = fields.Notes;
        employee.UpdatedUtc = DateTime.UtcNow;
    }
}

public class UpdateEmployeeHandler : IRequestHandler<UpdateEmployeeCommand, Unit>
{
    private readonly IEmployeeRepository _employees;

    private readonly IEmployeeIdentityProvider _identities;
    private readonly ITenantContext _tenant;
    private readonly IEmployeeIdentityLock _locks;
    public UpdateEmployeeHandler(IEmployeeRepository employees, IEmployeeIdentityProvider identities, ITenantContext tenant, IEmployeeIdentityLock locks)
    { _employees = employees; _identities = identities; _tenant = tenant; _locks = locks; }

    public async Task<Unit> Handle(UpdateEmployeeCommand request, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Employee", request.Id);

        await using var lease = await _locks.AcquireAsync(_tenant.TenantId, employee.Email, ct);
        employee = await _employees.ReloadAsync(request.Id, ct) ?? throw new NotFoundException("Employee", request.Id);
        if (!string.IsNullOrWhiteSpace(employee.KeycloakUserId) && !employee.Email.Equals(request.Fields.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new EmployeeIdentityConflictException("Changing a linked login email requires an identity reconciliation flow.");
        if (await _employees.GetByEmailAsync(request.Fields.Email.Trim(), ct) is { } existing && existing.Id != employee.Id)
            throw CreateEmployeeHandler.DuplicateEmail(existing);
        var accessChanged = employee.Role != request.Fields.Role || employee.IsActive != request.IsActive;
        CreateEmployeeHandler.Apply(employee, request.Fields);
        employee.IsActive = request.IsActive;
        employee.IdentitySyncPending = !string.IsNullOrWhiteSpace(employee.KeycloakUserId) && (accessChanged || employee.IdentitySyncPending);
        await _employees.SaveAsync(employee, ct);
        if (employee.IdentitySyncPending) {
            await _identities.SynchronizeAccessAsync(_tenant.TenantId, employee.KeycloakUserId, employee.Role, employee.IsActive, ct);
            employee.IdentitySyncPending = false; await _employees.SaveAsync(employee, ct);
        }
        return Unit.Value;
    }
}

public class ReactivateEmployeeHandler : IRequestHandler<ReactivateEmployeeCommand, Unit>
{
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeIdentityProvider _identities;
    private readonly ITenantContext _tenant;
    private readonly IEmployeeIdentityLock _locks;

    public ReactivateEmployeeHandler(IEmployeeRepository employees, IEmployeeIdentityProvider identities, ITenantContext tenant, IEmployeeIdentityLock locks)
    { _employees = employees; _identities = identities; _tenant = tenant; _locks = locks; }

    public async Task<Unit> Handle(ReactivateEmployeeCommand request, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Employee", request.Id);
        await using var lease = await _locks.AcquireAsync(_tenant.TenantId, employee.Email, ct);
        employee = await _employees.ReloadAsync(request.Id, ct) ?? throw new NotFoundException("Employee", request.Id);
        var accessChanged = !employee.IsActive;
        employee.IsActive = true;
        employee.IdentitySyncPending = !string.IsNullOrWhiteSpace(employee.KeycloakUserId) && (accessChanged || employee.IdentitySyncPending);
        employee.UpdatedUtc = DateTime.UtcNow;
        await _employees.SaveAsync(employee, ct);
        if (employee.IdentitySyncPending)
        {
            await _identities.SynchronizeAccessAsync(_tenant.TenantId, employee.KeycloakUserId, employee.Role, true, ct);
            employee.IdentitySyncPending = false;
            await _employees.SaveAsync(employee, ct);
        }
        return Unit.Value;
    }
}

public class DeactivateEmployeeHandler : IRequestHandler<DeactivateEmployeeCommand, Unit>
{
    private readonly IEmployeeRepository _employees;

    private readonly IEmployeeIdentityProvider _identities;
    private readonly ITenantContext _tenant;
    private readonly IEmployeeIdentityLock _locks;
    public DeactivateEmployeeHandler(IEmployeeRepository employees, IEmployeeIdentityProvider identities, ITenantContext tenant, IEmployeeIdentityLock locks)
    { _employees = employees; _identities = identities; _tenant = tenant; _locks = locks; }

    public async Task<Unit> Handle(DeactivateEmployeeCommand request, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Employee", request.Id);

        await using var lease = await _locks.AcquireAsync(_tenant.TenantId, employee.Email, ct);
        employee = await _employees.ReloadAsync(request.Id, ct) ?? throw new NotFoundException("Employee", request.Id);
        employee.IsActive = false;
        employee.IdentitySyncPending = !string.IsNullOrWhiteSpace(employee.KeycloakUserId);
        employee.UpdatedUtc = DateTime.UtcNow;
        await _employees.SaveAsync(employee, ct);
        if (employee.IdentitySyncPending) {
            await _identities.SynchronizeAccessAsync(_tenant.TenantId, employee.KeycloakUserId, employee.Role, false, ct);
            employee.IdentitySyncPending = false; await _employees.SaveAsync(employee, ct);
        }
        return Unit.Value;
    }
}

public sealed class DeleteEmployeeHandler(IEmployeeRepository employees, IShiftRepository shifts,
    IBookingRepository bookings, IQualityCycleRepository forms) : IRequestHandler<DeleteEmployeeCommand, Unit>
{
    public async Task<Unit> Handle(DeleteEmployeeCommand request, CancellationToken ct)
    {
        var employee = await employees.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Employee", request.Id);
        if (employee.IsActive) throw new InactiveRecordDeleteConflictException("Deactivate the employee before deleting the profile.");
        if (!string.IsNullOrWhiteSpace(employee.KeycloakUserId))
            throw new InactiveRecordDeleteConflictException("This employee is linked to a login account. Disable the account and retain the profile, or remove the login through identity administration first.");
        if ((await shifts.ListAssignmentHistoryByEmployeeAsync(employee.Id, ct)).Count > 0
            || (await bookings.HasHistoryForEmployeeAsync(employee.Id, ct))
            || (await forms.ListFormsAsync(null, null, null, ct, employee.Id, null, null, null)).Count > 0)
            throw new InactiveRecordDeleteConflictException("This employee has shift, booking, or checklist history and must be retained.");
        await employees.DeleteAsync(employee.Id, ct);
        return Unit.Value;
    }
}
