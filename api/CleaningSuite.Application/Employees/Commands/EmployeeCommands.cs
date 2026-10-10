using CleaningSuite.Application.Common;
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

public record InviteEmployeeCommand(Guid Id) : IRequest<EmployeeInviteResult>;

public record EmployeeInviteResult(string Email);

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
    private readonly ITenantContext _context;
    private readonly IKeycloakProvisioner _provisioner;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;

    public InviteEmployeeHandler(
        IEmployeeRepository employees,
        ITenantContext context,
        IKeycloakProvisioner provisioner,
        IEmailSender email,
        IConfiguration config)
    {
        _employees = employees;
        _context = context;
        _provisioner = provisioner;
        _email = email;
        _config = config;
    }

    public async Task<EmployeeInviteResult> Handle(InviteEmployeeCommand request, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException("Employee", request.Id);

        // Pre-create (or re-arm) the Keycloak user so the email is locked to the
        // invited address — the employee cannot change it during sign-in.
        var invited = await _provisioner.InviteEmployeeAsync(
            _context.TenantId,
            employee.Email,
            employee.FirstName,
            employee.LastName,
            employee.Role,
            ct);

        employee.KeycloakUserId = invited.KeycloakUserId;
        employee.InvitedAtUtc = DateTime.UtcNow;
        employee.UpdatedUtc = DateTime.UtcNow;
        await _employees.SaveAsync(employee, ct);

        var baseUrl = _config["App:FrontendBaseUrl"] ?? "https://readysetsiivous.fi";
        var loginUrl = $"{baseUrl.TrimEnd('/')}/fi/admin/";

        var html = BuildInviteEmail(employee.FirstName, employee.Email, invited.TemporaryPassword, loginUrl);
        await _email.SendAsync(employee.Email, "Welcome to ReadySetSiivous — set up your account", html, ct);

        return new EmployeeInviteResult(employee.Email);
    }

    private static string BuildInviteEmail(string firstName, string email, string temporaryPassword, string loginUrl)
    {
        var name = Escape(firstName);
        var safeEmail = Escape(email);
        var safePwd = Escape(temporaryPassword);
        var safeLink = Escape(loginUrl);
        return string.Join("\n",
            "<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:520px;margin:0 auto;padding:24px;color:#1a1a1a\">",
            "  <h2 style=\"margin:0 0 16px\">Welcome to ReadySetSiivous</h2>",
            $"  <p style=\"margin:0 0 16px\">Hi {name},</p>",
            $"  <p style=\"margin:0 0 16px\">You have been invited to ReadySetSiivous. Sign in with <strong>{safeEmail}</strong> and the temporary password below. You will be asked to choose a new password on first sign-in.</p>",
            $"  <p style=\"margin:0 0 8px;font-size:14px\">Temporary password: <strong>{safePwd}</strong></p>",
            $"  <a href=\"{safeLink}\" style=\"display:inline-block;background:#D9B95C;color:#070B1A;padding:12px 28px;border-radius:8px;font-weight:bold;text-decoration:none;font-size:15px\">Sign in</a>",
            $"  <p style=\"margin:20px 0 0;font-size:12px;color:#888888\">If the button doesn't work, go to: {safeLink}</p>",
            "</div>");
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
