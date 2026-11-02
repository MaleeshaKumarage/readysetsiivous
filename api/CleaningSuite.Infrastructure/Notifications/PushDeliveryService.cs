using CleaningSuite.Application.Notifications;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Notifications;

public sealed class PushDeliveryService(ITenantSession tenant, IPushDeliveryReservation reservation, IPushSettings settings,
    IPushTransport transport, IPushDeliveryEligibility eligibility, TimeProvider clock) : IPushDeliveryService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task ProcessAsync(CancellationToken ct = default)
    {
        if (!settings.Available) return;
        var intents = await tenant.Session.Query<PushIntent>().Where(i => !i.Materialized && !i.Cancelled).OrderBy(i => i.DueUtc).Take(100).ToListAsync(ct);
        foreach (var intent in intents.Where(i => i.DueUtc <= Now))
        {
            await using var lease = await reservation.TryAcquireAsync(intent.Id, ct);
            if (lease is null) continue;
            tenant.Reset();
            var current = await tenant.Session.LoadAsync<PushIntent>(intent.Id, ct);
            if (current is null || current.Materialized || current.Cancelled) continue;
            if (current.ExpiresUtc > Now)
            {
                var devices = await tenant.Session.Query<PushDevice>().Where(d => d.EmployeeId == current.EmployeeId && d.Enabled).ToListAsync(ct);
                foreach (var device in devices.Where(d => d.OptedInUtc <= current.OccurredUtc && d.CreatedUtc <= current.OccurredUtc))
                {
                    var id = PushValidation.Id($"delivery:{current.Id}:{device.Id}");
                    if (await tenant.Session.LoadAsync<PushDelivery>(id, ct) is null)
                        tenant.Session.Store(new PushDelivery { Id = id, IntentId = current.Id, DeviceId = device.Id, NextAttemptUtc = current.DueUtc });
                }
            }
            current.Materialized = true; await tenant.Session.SaveChangesAsync(ct);
        }
        var deliveries = await tenant.Session.Query<PushDelivery>().Where(d => d.State == "Pending" || d.State == "Sending").OrderBy(d => d.NextAttemptUtc).Take(100).ToListAsync(ct);
        foreach (var delivery in deliveries)
        {
            if (delivery.NextAttemptUtc > Now || delivery.State == "Sending" && delivery.AttemptUtc > Now.AddMinutes(-2)) continue;
            await Deliver(delivery.Id, ct);
        }
        // Keep ownership tombstones, but remove disabled delivery credentials after retention.
        var retentionCutoff = DateTime.SpecifyKind(Now.AddDays(-30), DateTimeKind.Unspecified);
        var old = await tenant.Session.Query<PushDevice>()
            .Where(d => !d.Enabled && d.Endpoint != "" && d.UpdatedUtc < retentionCutoff)
            .OrderBy(d => d.UpdatedUtc).Take(100).ToListAsync(ct);
        foreach (var device in old) { device.Endpoint = ""; device.P256dh = ""; device.Auth = ""; }
        await tenant.Session.SaveChangesAsync(ct);
    }
    private async Task Deliver(Guid id, CancellationToken ct)
    {
        await using var lease = await reservation.TryAcquireAsync(id, ct);
        if (lease is null) return;
        tenant.Reset();
        var delivery = await tenant.Session.LoadAsync<PushDelivery>(id, ct);
        if (delivery is null || delivery.State is not ("Pending" or "Sending") || delivery.NextAttemptUtc > Now || delivery.State == "Sending" && delivery.AttemptUtc > Now.AddMinutes(-2)) return;
        var intent = await tenant.Session.LoadAsync<PushIntent>(delivery.IntentId, ct);
        var device = await tenant.Session.LoadAsync<PushDevice>(delivery.DeviceId, ct);
        if (intent is not null && intent.DueUtc > Now) { delivery.NextAttemptUtc = intent.DueUtc; delivery.State = "Pending"; await tenant.Session.SaveChangesAsync(ct); return; }
        var employee = device is null ? null : await tenant.Session.LoadAsync<Employee>(device.EmployeeId, ct);
        if (intent is null || device is null || employee is null || intent.Cancelled || intent.ExpiresUtc <= Now || delivery.Attempts >= 5
            || !device.Enabled || device.VapidKeyId != settings.KeyId || !employee.IsActive || employee.IdentitySyncPending
            || !Employee.IsWorkRole(employee.Role) || employee.KeycloakUserId != device.Subject || string.IsNullOrEmpty(device.Subject)
            || device.EmployeeId != intent.EmployeeId || device.OptedInUtc > intent.OccurredUtc || intent.AdminAudience && employee.Role != Employee.RoleAdmin
            || !await eligibility.IsEligibleAsync(intent, ct))
        {
            delivery.State = "Cancelled"; delivery.FailureCode = "ineligible_or_expired"; await tenant.Session.SaveChangesAsync(ct); return;
        }
        delivery.State = "Sending"; delivery.Attempts++; delivery.AttemptUtc = Now;
        await tenant.Session.SaveChangesAsync(ct);
        var body = intent.Kind switch { "Upcoming" => "Your scheduled work starts soon. Open My shifts for details.", "Unfinished" => "Your work is still running. Open My shifts to finish or submit it.", "PartnerRequest" => "A new partner cleaning request is ready for review.", "PartnerOffer" => "New work is available for your approval. Open your account to review it.", _ => "Your work schedule has changed. Open My shifts for the latest details." };
        var path = intent.AdminAudience ? "/admin/partner-requests/" : intent.Kind == "PartnerOffer" ? "/account/" : "/me/shifts/";
        var result = await transport.SendAsync(device, new("ReadySetSiivous", body, path + "?tenant=" + Uri.EscapeDataString(tenant.TenantId), "push-" + intent.Id.ToString("N")), intent.ExpiresUtc, ct);
        if (result.Accepted) delivery.State = "Accepted";
        else if (result.Expired) { delivery.State = "Failed"; delivery.FailureCode = "subscription_expired"; device.Enabled = false; device.UpdatedUtc = Now; }
        else if (result.Retryable && delivery.Attempts < 5)
        {
            var backoff = TimeSpan.FromSeconds(Math.Pow(2, delivery.Attempts) * 15);
            if (result.RetryAfter > backoff) backoff = result.RetryAfter.Value;
            delivery.NextAttemptUtc = Now.Add(backoff > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : backoff);
            delivery.State = delivery.NextAttemptUtc < intent.ExpiresUtc ? "Pending" : "Failed"; delivery.FailureCode = "transient_provider_failure";
        }
        else { delivery.State = "Failed"; delivery.FailureCode = "provider_rejected"; }
        await tenant.Session.SaveChangesAsync(ct);
    }
}
