using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Employees;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Employees.Commands;

public record EmployeeFields(
    string Email,
    string FirstName,
    string LastName,
    string Phone,
    string Role,
    string? ColorHex,
    Dictionary<string, WorkHours> DefaultHours);

public record CreateEmployeeCommand(EmployeeFields Fields) : IRequest<Guid>;

public record UpdateEmployeeCommand(Guid Id, EmployeeFields Fields, bool IsActive) : IRequest<Unit>;

public record DeactivateEmployeeCommand(Guid Id) : IRequest<Unit>;

public record InviteEmployeeCommand(Guid Id) : IRequest<EmployeeInviteResult>;

public record EmployeeInviteResult(string Email, string TemporaryPassword);

public record AssignBookingCommand(Guid BookingId, Guid EmployeeId) : IRequest<Unit>;

public record UnassignBookingCommand(Guid BookingId) : IRequest<Unit>;

public class EmployeeFieldsValidator : AbstractValidator<EmployeeFields>
{
    public EmployeeFieldsValidator()
    {
        RuleFor(x => x.Email).EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).Must(r => r is Employee.RoleAdmin or Employee.RoleEmployee);
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
        if (await _employees.GetByEmailAsync(request.Fields.Email, ct) is not null)
            throw new SlugConflictException($"Employee {request.Fields.Email} already exists");

        var employee = new Employee();
        Apply(employee, request.Fields);
        await _employees.SaveAsync(employee, ct);
        return employee.Id;
    }

    internal static void Apply(Employee employee, EmployeeFields fields)
    {
        employee.Email = fields.Email;
        employee.FirstName = fields.FirstName;
        employee.LastName = fields.LastName;
        employee.Phone = fields.Phone;
        employee.Role = fields.Role;
        employee.ColorHex = fields.ColorHex;
        employee.DefaultHours = fields.DefaultHours;
        employee.UpdatedUtc = DateTime.UtcNow;
    }
}

public class UpdateEmployeeHandler : IRequestHandler<UpdateEmployeeCommand, Unit>
{
    private readonly IEmployeeRepository _employees;

    public UpdateEmployeeHandler(IEmployeeRepository employees) => _employees = employees;

    public async Task<Unit> Handle(UpdateEmployeeCommand request, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Employee", request.Id);

        CreateEmployeeHandler.Apply(employee, request.Fields);
        employee.IsActive = request.IsActive;
        await _employees.SaveAsync(employee, ct);
        return Unit.Value;
    }
}

public class DeactivateEmployeeHandler : IRequestHandler<DeactivateEmployeeCommand, Unit>
{
    private readonly IEmployeeRepository _employees;

    public DeactivateEmployeeHandler(IEmployeeRepository employees) => _employees = employees;

    public async Task<Unit> Handle(DeactivateEmployeeCommand request, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Employee", request.Id);

        employee.IsActive = false;
        employee.UpdatedUtc = DateTime.UtcNow;
        await _employees.SaveAsync(employee, ct);
        return Unit.Value;
    }
}

public class InviteEmployeeHandler : IRequestHandler<InviteEmployeeCommand, EmployeeInviteResult>
{
    private readonly IEmployeeRepository _employees;
    private readonly IKeycloakProvisioner _keycloak;
    private readonly ITenantContext _context;

    public InviteEmployeeHandler(
        IEmployeeRepository employees,
        IKeycloakProvisioner keycloak,
        ITenantContext context)
    {
        _employees = employees;
        _keycloak = keycloak;
        _context = context;
    }

    public async Task<EmployeeInviteResult> Handle(InviteEmployeeCommand request, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Employee", request.Id);

        var result = await _keycloak.InviteEmployeeAsync(
            _context.TenantId,
            employee.Email,
            employee.FirstName,
            employee.LastName,
            employee.Role,
            ct);

        employee.KeycloakUserId = result.KeycloakUserId;
        employee.UpdatedUtc = DateTime.UtcNow;
        await _employees.SaveAsync(employee, ct);

        return new EmployeeInviteResult(employee.Email, result.TemporaryPassword);
    }
}
