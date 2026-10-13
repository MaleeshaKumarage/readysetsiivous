using System.Net.Mail;
using System.Text.RegularExpressions;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Application.Employees.Commands;
public class InviteEmployeeHandler(IEmployeeRepository employees, ITenantContext tenant,
    IEmployeeIdentityProvider identities, IEmployeeIdentityLock locks, IConfiguration configuration, TimeProvider clock)
    : IRequestHandler<InviteEmployeeCommand, EmployeeInviteResult>
{
    public async Task<EmployeeInviteResult> Handle(InviteEmployeeCommand request, CancellationToken ct)
    {
        var employee = await employees.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Employee", request.Id);
        var email = employee.Email;
        await using var lease = await locks.AcquireAsync(tenant.TenantId, email, ct);
        employee = await employees.ReloadAsync(request.Id, ct) ?? throw new NotFoundException("Employee", request.Id);
        if (!employee.Email.Trim().Equals(email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new EmployeeIdentityConflictException("Employee email changed while inviting. Retry with the current record.");
        if (employee.IdentitySyncPending) throw new EmployeeIdentityConflictException("Finish the pending login synchronization before sending another invitation.");
        if (!employee.IsActive) throw new EmployeeIdentityConflictException("Inactive employees cannot be invited.");
        if (employee.Role is not (Employee.RoleAdmin or Employee.RoleEmployee)) throw new EmployeeIdentityConflictException("Unsupported employee role.");
        if (!MailAddress.TryCreate(employee.Email, out var address) || address.Address != employee.Email.Trim())
            throw new EmployeeIdentityConflictException("The employee's email address is invalid.");
        var emailOwner = await employees.GetByEmailAsync(employee.Email, ct);
        if (emailOwner is not null && emailOwner.Id != employee.Id)
            throw new EmployeeIdentityConflictException("Another employee record uses this email. Reconcile ownership before inviting.");
        var frontend = BaseUrl("App:FrontendBaseUrl");
        _ = BaseUrl("Auth:Keycloak:ServerUrl");
        if (!Regex.IsMatch(tenant.TenantId, "^[a-z0-9][a-z0-9-]{2,62}$")) throw new EmployeeInviteConfigurationException("A valid tenant realm is required.");
        if (!int.TryParse(configuration["EmployeeInvites:LifespanSeconds"] ?? "86400", out var lifespan) || lifespan is < 300 or > 604800) throw new EmployeeInviteConfigurationException("Invitation lifespan must be between5minutes and7days.");
        var redirect = $"{frontend}/account/?tenant={Uri.EscapeDataString(tenant.TenantId)}";
        await identities.ValidateInvitationAsync(tenant.TenantId, redirect, employee.Role, ct);
        var identity = await identities.EnsureAsync(tenant.TenantId, employee, ct);
        var other = await employees.GetByKeycloakUserIdAsync(identity.UserId, ct);
        if (other is not null && other.Id != employee.Id) throw new EmployeeIdentityConflictException("This login is already linked to another employee.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (employee.InviteLastSentUtc > now.AddSeconds(-60) && employee.KeycloakUserId == identity.UserId) {
            await identities.AssignRoleAsync(tenant.TenantId, identity.UserId, employee.Role, ct);
            return new(employee.Email, identity.UserId, "AlreadySent", employee.InviteLastSentUtc);
        }
        employee.KeycloakUserId = identity.UserId;
        employee.InviteLastAttemptUtc = now; employee.InviteError = null; employee.UpdatedUtc = now;
        // Persist ownership before external delivery. A failed send can be retried safely.
        await employees.SaveAsync(employee, ct);
        try {
            // Grant access only after the local binding exists, so database failure
            // cannot create an unlinked account with tenant-owner compatibility access.
            await identities.AssignRoleAsync(tenant.TenantId, identity.UserId, employee.Role, ct);
            await identities.SendSetupEmailAsync(tenant.TenantId, identity.UserId, redirect, lifespan, ct);
        } catch (EmployeeIdentityProviderException) {
            employee.InviteError = "Login setup or email failed; retry invitation";
            await employees.SaveAsync(employee, ct); throw;
        }
        employee.InviteLastSentUtc = clock.GetUtcNow().UtcDateTime;
        await employees.SaveAsync(employee, ct);
        return new(employee.Email, identity.UserId, "Sent", employee.InviteLastSentUtc);
    }
    private string BaseUrl(string key) {
        var value = configuration[key];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new EmployeeInviteConfigurationException($"Configure a valid {key} before inviting employees.");
        if (key == "App:FrontendBaseUrl" && uri.AbsolutePath != "/")
            throw new EmployeeInviteConfigurationException("App:FrontendBaseUrl must be the public site origin, without /admin or a language path.");
        return uri.AbsoluteUri.TrimEnd('/');
    }
}
