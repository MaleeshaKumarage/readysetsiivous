using CleaningSuite.Application.Notifications;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Notifications;

public sealed class PushSubscriptions(ITenantSession tenant, IShiftWorkReservation reservation, IPushSettings settings, IPushTransport transport, TimeProvider clock) : IPushSubscriptions
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task Verify(PushActor actor, CancellationToken ct)
    {
        var employee = await tenant.Session.LoadAsync<Employee>(actor.EmployeeId, ct);
        if (employee is null || !employee.IsActive || employee.IdentitySyncPending || !Employee.IsWorkRole(employee.Role) || string.IsNullOrEmpty(actor.Subject) || employee.KeycloakUserId != actor.Subject) throw new PushSubscriptionException("employee_ineligible");
    }
    public async Task<PushConfiguration> GetAsync(PushActor actor, CancellationToken ct = default)
    {
        await Verify(actor, ct);
        var devices = await tenant.Session.Query<PushDevice>().Where(d => d.EmployeeId == actor.EmployeeId && d.Subject == actor.Subject).ToListAsync(ct);
        return new(settings.Available, settings.Available ? settings.PublicKey : null, devices.Select(d => new PushDeviceStatus(d.Id, d.Enabled && d.VapidKeyId == settings.KeyId)).ToList());
    }
    public async Task<PushDeviceStatus> EnrollAsync(PushActor actor, PushSubscriptionInput input, CancellationToken ct = default)
    {
        if (!settings.Available) throw new PushSubscriptionException("unavailable");
        if (!PushValidation.Endpoint(input.Endpoint) || !PushValidation.Keys(input.Keys)) throw new PushSubscriptionException("invalid_subscription");
        await using var lease = await reservation.AcquireAsync(ct);
        await Verify(actor, ct);
        var id = PushValidation.Id(input.Endpoint);
        var device = await tenant.Session.LoadAsync<PushDevice>(id, ct);
        if (device is not null && (device.EmployeeId != actor.EmployeeId || device.Subject != actor.Subject)) throw new PushSubscriptionException("ownership_conflict");
        var obsolete = await tenant.Session.Query<PushDevice>().Where(d => d.EmployeeId == actor.EmployeeId && d.Subject == actor.Subject && d.Enabled && d.VapidKeyId != settings.KeyId).ToListAsync(ct);
        foreach (var previous in obsolete)
        { previous.Enabled = false; previous.UpdatedUtc = Now; tenant.Session.Store(previous); }
        if (device is null || !device.Enabled)
        {
            var count = await tenant.Session.Query<PushDevice>().CountAsync(d => d.EmployeeId == actor.EmployeeId && d.Enabled && d.VapidKeyId == settings.KeyId, ct);
            if (count >= 10) throw new PushSubscriptionException("device_limit");
        }
        device ??= new PushDevice { Id = id, EmployeeId = actor.EmployeeId, Subject = actor.Subject, CreatedUtc = Now, OptedInUtc = Now };
        if (!device.Enabled || device.VapidKeyId != settings.KeyId) device.OptedInUtc = Now;
        device.Endpoint = input.Endpoint; device.P256dh = input.Keys.P256dh; device.Auth = input.Keys.Auth;
        device.VapidKeyId = settings.KeyId; device.Enabled = true; device.UpdatedUtc = Now;
        tenant.Session.Store(device); await tenant.Session.SaveChangesAsync(ct);
        return new(device.Id, true);
    }
    private async Task<PushDevice> Own(PushActor actor, Guid id, CancellationToken ct)
    {
        await Verify(actor, ct);
        var device = await tenant.Session.LoadAsync<PushDevice>(id, ct);
        if (device is null || device.EmployeeId != actor.EmployeeId || device.Subject != actor.Subject) throw new PushSubscriptionException("device_not_found");
        return device;
    }
    public async Task DisableAsync(PushActor actor, Guid id, CancellationToken ct = default)
    {
        await using var lease = await reservation.AcquireAsync(ct);
        var device = await Own(actor, id, ct); device.Enabled = false; device.UpdatedUtc = Now;
        await tenant.Session.SaveChangesAsync(ct);
    }
    public async Task<PushTestResult> TestAsync(PushActor actor, Guid id, CancellationToken ct = default)
    {
        if (!settings.Available) throw new PushSubscriptionException("unavailable");
        PushDevice device;
        await using (var lease = await reservation.AcquireAsync(ct))
        {
            device = await Own(actor, id, ct);
            if (!device.Enabled || device.VapidKeyId != settings.KeyId) throw new PushSubscriptionException("device_not_found");
            if (device.LastTestUtc > Now.AddMinutes(-1)) throw new PushSubscriptionException("rate_limited");
            device.LastTestUtc = Now; await tenant.Session.SaveChangesAsync(ct);
        }
        var result = await transport.SendAsync(device, new("ReadySetSiivous", "Notifications are ready on this device.", "/me/shifts/?tenant=" + Uri.EscapeDataString(tenant.TenantId), "push-test"), Now.AddMinutes(1), ct);
        if (result.Expired) { device.Enabled = false; device.UpdatedUtc = Now; await tenant.Session.SaveChangesAsync(ct); }
        return new(result.Accepted);
    }
}
