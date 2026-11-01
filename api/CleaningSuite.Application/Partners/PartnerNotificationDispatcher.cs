using System.Net;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace CleaningSuite.Application.Partners;

/// <summary>Delivery follows commit. Cross-replica reservation protects persisted payloads and provider sends.</summary>
public sealed class PartnerNotificationDispatcher(IPartnerStore store, IShiftWorkReservation reservation,
    IPartnerNotificationTransport transport, ITenantRegistry registry, ITenantContext tenant,
    TimeProvider clock, IConfiguration config, ILogger<PartnerNotificationDispatcher> logger,
    IPartnerNotificationReservation deliveries)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private IDocumentSession Db => store.Session;

    public async Task DispatchAsync(CancellationToken ct)
    {
        if (!await TenantActive(ct)) return;
        await ReconcileWorkAsync(ct);
        // Existing Marten JSON DateTime comparisons use timestamp without time
        // zone. Keep UTC clock ticks, matching that mapping only at the query
        // boundary; the persisted notification and retry times remain UTC.
        var dueBefore = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified);
        var ids = await Db.Query<PartnerNotification>().Where(n => n.State == "Pending" && n.NextAttemptUtc <= dueBefore)
            .OrderBy(n => n.NextAttemptUtc).Take(100).Select(n => n.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await DispatchAsync(id, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) { logger.LogError(error, "Partner notification {NotificationId} failed", id); }
        }
    }

    public async Task DispatchAsync(Guid id, CancellationToken ct)
    {
        // Lock order is intent then scheduling. Only short database phases reserve scheduling;
        // a slow provider must never prevent a cleaner from starting or ending a shift.
        await using var deliveryLease = await deliveries.TryAcquireAsync(id, ct);
        if (deliveryLease is null) return;
        PartnerNotification item;
        await using (var work = await reservation.AcquireAsync(ct))
        {
            var loaded = await CurrentIntent(id, ct);
            if (loaded is null) return;
            item = loaded;
            if (item.FirstAttemptUtc.HasValue && Now - item.FirstAttemptUtc.Value >= TimeSpan.FromHours(20))
            { await Finish(item, "NeedsReview", "Safe provider idempotency retry window expired. Check delivery before resending.", ct); return; }
            if (!item.FirstAttemptUtc.HasValue)
            {
                var company = (await Db.LoadAsync<Company>(item.CompanyId, ct))!;
                var request = item.RequestId.HasValue ? await Db.LoadAsync<PartnerCleaningRequest>(item.RequestId.Value, ct) : null;
                var branch = await Db.LoadAsync<Branch>(item.BranchId, ct);
                var employees = await Db.Query<Employee>().Where(e => e.IsActive && !e.IdentitySyncPending).ToListAsync(ct);
                item.Deliveries = employees.Where(e => Eligible(item, e, company.IsPartnerEnabled))
                    .Where(e => System.Net.Mail.MailAddress.TryCreate(e.Email, out _))
                    .OrderBy(e => e.Id).Select(e => Payload(item, e, branch?.Name ?? "Property", request)).ToList();
                if (item.Deliveries.Count == 0) { await Finish(item, "Skipped", "No eligible notification recipients.", ct); return; }
            }
            item.FirstAttemptUtc ??= Now; item.Attempts++; item.UpdatedUtc = Now;
            Db.Store(item); await Db.SaveChangesAsync(ct); // Freeze complete payloads before provider I/O.
        }
        try
        {
            foreach (var deliveryId in item.Deliveries.Where(d => !d.SentUtc.HasValue && !d.SkippedUtc.HasValue && !d.FailedUtc.HasValue).Select(d => d.Id).ToArray())
            {
                PartnerNotificationDelivery delivery;
                await using (var work = await reservation.AcquireAsync(ct))
                {
                    // Reservation refreshes the session: each recipient is checked against current state,
                    // including changes made while a previous recipient's provider request was in flight.
                    var loaded = await CurrentIntent(id, ct);
                    if (loaded is null) return;
                    item = loaded;
                    delivery = item.Deliveries.Single(d => d.Id == deliveryId);
                    var company = (await Db.LoadAsync<Company>(item.CompanyId, ct))!;
                    var current = await Db.LoadAsync<Employee>(delivery.RecipientEmployeeId, ct);
                    if (current is null || !Eligible(item, current, company.IsPartnerEnabled) || !current.Email.Equals(delivery.To, StringComparison.OrdinalIgnoreCase))
                    {
                        delivery.SkippedUtc = Now; delivery.SkipReason = "Recipient permissions or email changed.";
                        Db.Store(item); await Db.SaveChangesAsync(ct); continue;
                    }
                }
                PartnerNotificationSendException? rejected = null;
                try { await transport.SendAsync(delivery, ct); }
                catch (PartnerNotificationSendException error) when (!error.Retryable) { rejected = error; }
                await using (var work = await reservation.AcquireAsync(ct))
                {
                    item = (await Db.LoadAsync<PartnerNotification>(id, ct))!;
                    var saved = item.Deliveries.Single(d => d.Id == deliveryId);
                    if (rejected is null) saved.SentUtc = Now;
                    else { saved.FailedUtc = Now; saved.FailureReason = rejected.Message; }
                    Db.Store(item); await Db.SaveChangesAsync(ct);
                }
            }
            await using (var work = await reservation.AcquireAsync(ct))
            {
                item = (await Db.LoadAsync<PartnerNotification>(id, ct))!;
                var anySent = item.Deliveries.Any(d => d.SentUtc.HasValue);
                var anyFailed = item.Deliveries.Any(d => d.FailedUtc.HasValue);
                item.SentUtc = anySent ? Now : null;
                await Finish(item, anyFailed ? "Failed" : anySent ? "Sent" : "Skipped", anyFailed ? "One or more recipients could not be delivered. Review individual delivery results." : null, ct);
            }
        }
        catch (PartnerNotificationSendException error)
        {
            await using var work = await reservation.AcquireAsync(ct);
            item = (await Db.LoadAsync<PartnerNotification>(id, ct))!;
            item.LastError = error.Message;
            if (!error.Retryable) item.State = "Failed";
            else if (item.Attempts >= 12) item.State = "NeedsReview";
            else
            {
                var delay = error.RetryAfter ?? TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(item.Attempts, 6))));
                delay = delay < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
                item.NextAttemptUtc = Now.Add(delay > TimeSpan.FromHours(20) ? TimeSpan.FromHours(20) : delay);
            }
            item.UpdatedUtc = Now; Db.Store(item); await Db.SaveChangesAsync(ct);
        }
    }

    // Called only inside a short scheduling reservation, never while awaiting the provider.
    private async Task<PartnerNotification?> CurrentIntent(Guid id, CancellationToken ct)
    {
        if (!await TenantActive(ct)) return null;
        var item = await Db.LoadAsync<PartnerNotification>(id, ct);
        if (item is null || item.State != "Pending" || item.NextAttemptUtc > Now) return null;
        var company = await Db.LoadAsync<Company>(item.CompanyId, ct);
        var request = item.RequestId.HasValue ? await Db.LoadAsync<PartnerCleaningRequest>(item.RequestId.Value, ct) : null;
        if (company is null || !company.IsActive || (item.RequestId.HasValue && request is null))
        { await Finish(item, "Skipped", "Company or request is no longer available.", ct); return null; }
        if (item.Audience == "Employee" && !CurrentForRequest(item, request))
        { await Finish(item, "Skipped", "This cleaner offer is no longer current.", ct); return null; }
        return item;
    }

    public static bool CurrentForRequest(PartnerNotification item, PartnerCleaningRequest? request) => request is not null && (item.Kind switch
    {
        "OfferCreated" => request.Status == "Offered" && request.Revision == item.Revision && request.OfferedEmployeeId == item.RecipientEmployeeId,
        "RequestCancelled" => request.Status == "Cancelled",
        "OfferWithdrawn" => request.Revision >= item.Revision
            && !(request.Status == "Offered" && request.OfferedEmployeeId == item.RecipientEmployeeId)
            && !(request.Status == "Accepted" && request.AcceptedEmployeeId == item.RecipientEmployeeId),
        _ => true
    });

    public static bool Eligible(PartnerNotification notification, Employee employee, bool partnerEnabled) =>
        employee.IsActive && !employee.IdentitySyncPending && (notification.Audience switch
        {
            "Staff" => employee.Role is Employee.RoleAdmin or Employee.RoleManager,
            "Company" => partnerEnabled && employee.Role == Employee.RolePartner && employee.PartnerCompanyId == notification.CompanyId,
            "Employee" => Employee.IsWorkRole(employee.Role) && employee.Id == notification.RecipientEmployeeId,
            _ => false
        });

    private PartnerNotificationDelivery Payload(PartnerNotification item, Employee employee, string branchName, PartnerCleaningRequest? request)
    {
        var origin = config["App:FrontendBaseUrl"];
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("Configure App:FrontendBaseUrl for partner notifications.");
        var route = item.Audience == "Company" ? "/partner/" : item.Audience == "Employee" ? "/me/shifts/" : employee.Role == Employee.RoleManager ? "/manager/partner-requests/" : "/admin/partner-requests/";
        var link = origin!.TrimEnd('/') + route;
        var title = item.Kind switch { "WorkStarted" => "Cleaning has started", "WorkCompleted" => "Cleaning is complete", _ => "Cleaning request update" };
        var dates = request is null ? "" : $"<p>Requested dates: {request.FromDate:yyyy-MM-dd} to {request.ToDate:yyyy-MM-dd}.</p>";
        return new() { Id = PartnerCleaningRequest.StableId($"partner-delivery:{item.Id:N}:{employee.Id:N}"), RecipientEmployeeId = employee.Id,
            To = employee.Email, Subject = $"ReadySetSiivous — {title}",
            Html = $"<div style='font-family:Arial,sans-serif;max-width:600px;padding:24px;color:#183b32'><h1>{title}</h1><p>There is an update for {WebUtility.HtmlEncode(branchName)}.</p>{dates}<p>Sign in to review the current status and next steps.</p><p><a style='background:#22634b;color:white;padding:12px 20px;text-decoration:none;display:inline-block' href='{WebUtility.HtmlEncode(link)}'>Open ReadySetSiivous</a></p><p>Property access instructions and private notes are available only after signing in.</p></div>" };
    }

    private async Task ReconcileWorkAsync(CancellationToken ct)
    {
        // Page through all accepted visits so completed visits beyond the first page are never starved.
        var page = 0;
        while (true)
        {
            var ids = await Db.Query<PartnerCleaningRequest>().Where(r => r.Status == "Accepted" && r.LinkedShiftId != null)
                .OrderBy(r => r.Id).Skip(page++ * 100).Take(100).Select(r => r.Id).ToListAsync(ct);
            if (ids.Count == 0) break;
            foreach (var id in ids)
            {
                await using var lease = await reservation.AcquireAsync(ct);
                var request = await Db.LoadAsync<PartnerCleaningRequest>(id, ct);
                if (request is null || request.Status != "Accepted" || !request.LinkedShiftId.HasValue) continue;
                var forms = await Db.Query<QualityCycleForm>().Where(f => f.ShiftId == request.LinkedShiftId.Value && f.ShiftDeletedUtc == null).ToListAsync(ct);
                var assignments = await Db.Query<ShiftAssignment>().Where(a => a.ShiftId == request.LinkedShiftId.Value && a.IsActive).ToListAsync(ct);
                var started = forms.Any(f => f.StartedAtUtc.HasValue);
                var complete = assignments.Count > 0 && assignments.All(a => forms.Any(f => f.EmployeeId == a.EmployeeId && f.IsSubmitted));
                foreach (var kind in new[] { started ? "WorkStarted" : null, complete ? "WorkCompleted" : null }.OfType<string>())
                    foreach (var audience in new[] { "Company", "Staff" })
                    {
                        var eventId = PartnerCleaningRequest.StableId($"partner-work:{request.Id:N}:{kind}:{audience}");
                        if (await Db.LoadAsync<PartnerNotification>(eventId, ct) is null)
                            Db.Store(new PartnerNotification { Id = eventId, CompanyId = request.CompanyId, BranchId = request.BranchId, RequestId = request.Id,
                                Audience = audience, Kind = kind, Revision = request.Revision, CreatedUtc = Now, UpdatedUtc = Now, NextAttemptUtc = Now });
                    }
                await Db.SaveChangesAsync(ct);
            }
        }
    }
    private async Task<bool> TenantActive(CancellationToken ct) => (await registry.GetBySlugAsync(tenant.TenantId, ct))?.Status == TenantRegistration.StatusActive;
    private async Task Finish(PartnerNotification item, string state, string? error, CancellationToken ct)
    { item.State = state; item.LastError = error; item.UpdatedUtc = Now; Db.Store(item); await Db.SaveChangesAsync(ct); }
}
