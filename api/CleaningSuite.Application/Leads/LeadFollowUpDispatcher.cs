using System.Net;
using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Leads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CleaningSuite.Domain.Tenants;
namespace CleaningSuite.Application.Leads;

public sealed class LeadFollowUpDispatcher(ILeadRepository leads,ILeadCollaborationRepository repository,ILeadAdminDirectory directory,
    ILeadReminderTransport transport,ITenantProfileRepository profiles,ITenantContext tenant,TimeProvider clock,IConfiguration config,ITenantRegistry registry,ILogger<LeadFollowUpDispatcher> logger)
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    public async Task DispatchAsync(CancellationToken ct)
    {
        foreach(var item in await repository.DueAsync(Now,ct)) {
            try { await DispatchAsync(item.LeadId,item.Id,ct); }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
            catch(Exception error) {
                logger.LogError(error,"Lead follow-up {FollowUpId} processing failed",item.Id);
                try {
                    await using var retryReservation=await leads.LockAsync(item.LeadId,ct);
                    var failed=await repository.FollowUpAsync(item.LeadId,item.Id,ct);
                    if(failed?.State=="Active") { failed.LastError=error is LeadConflictException?error.Message:"Follow-up processing failed. Check server configuration and retry status shortly.";failed.LastCheckedUtc=Now;failed.NextCheckUtc=Now.AddMinutes(5);await repository.SaveAsync(failed,[],ct); }
                } catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
                catch(Exception failure) { logger.LogError(failure,"Could not record follow-up processing failure for {FollowUpId}",item.Id); }
            }
        }
    }
    public async Task DispatchAsync(Guid leadId,Guid id,CancellationToken ct)
    {
        // The database reservation covers both provider send and lifecycle mutation. No
        // new send can start after a successful cancel/convert response on another replica.
        if((await registry.GetBySlugAsync(tenant.TenantId,ct))?.Status!=TenantRegistration.StatusActive) return;
        await using var reservation=await leads.LockAsync(leadId,ct);
        var follow=await repository.FollowUpAsync(leadId,id,ct);
        if(follow is null || follow.State!="Active" || follow.NextCheckUtc>Now) return;
        var lead=await leads.GetAsync(leadId,ct);
        var pending=(await repository.PendingAsync(id,ct)).ToList();
        var changes=new List<LeadFollowUpDelivery>();changes.AddRange(pending);
        if(lead is null || !lead.IsActive || lead.CompanyId.HasValue || lead.Fields.Status=="Lost") {
            follow.State="Cancelled";follow.NextDate=null;
            foreach(var d in pending) { d.State="Cancelled";d.Error="Lead closed, inactive or converted."; }
            await repository.SaveAsync(follow,changes,ct);return;
        }
        var (zoneId,zone)=await TenantTimeZone.LoadAsync(profiles,tenant,ct);
        var today=TenantTimeZone.LocalDate(Now,zone);
        if(follow.EndDate<today) {
            follow.NextDate=null;follow.State="Completed";
            foreach(var d in pending) { d.State=d.FirstAttemptUtc.HasValue?"Needs review":"Skipped";d.Error="Schedule end date has passed."; }
            await repository.SaveAsync(follow,changes,ct);return;
        }
        follow.LastCheckedUtc=Now;
        IReadOnlyList<LeadAdminRecipient> recipients;
        try { recipients=await directory.ListAsync(ct); }
        catch(LeadConflictException failure) { follow.LastError=failure.Message;follow.NextCheckUtc=Now.AddMinutes(5);await repository.SaveAsync(follow,[],ct);return; }

        follow.LastError=null;
        if(follow.NextDate is { } firstDue && LeadRecurrence.DueUtc(firstDue,zone)<=Now) {
            var latest=firstDue;var next=LeadRecurrence.Next(follow.FirstDate,latest,follow.Recurrence,follow.Interval);var skipped=0;
            while(next.HasValue && (!follow.EndDate.HasValue || next<=follow.EndDate) && LeadRecurrence.DueUtc(next.Value,zone)<=Now) {
                latest=next.Value;skipped++;next=LeadRecurrence.Next(follow.FirstDate,latest,follow.Recurrence,follow.Interval);
            }
            if(next>follow.EndDate) next=null;
            foreach(var earlier in pending.Where(x=>x.LocalDate<latest)) {
                earlier.State=earlier.FirstAttemptUtc.HasValue?"Needs review":"Skipped";
                earlier.Error="A newer recurring occurrence is due. Earlier notification was not resent.";
            }
            foreach(var recipientId in follow.RecipientIds) {
                var recipient=recipients.SingleOrDefault(x=>x.Id==recipientId);
                if(skipped>0) changes.Add(Delivery(lead.Fields.Name,follow,firstDue,zoneId,zone,recipientId,recipient,$"{skipped} earlier overdue occurrence(s) skipped after downtime."));
                var delivery=Delivery(lead.Fields.Name,follow,latest,zoneId,zone,recipientId,recipient,recipient is null?"Recipient is no longer an eligible tenant admin.":null);
                changes.Add(delivery);if(delivery.State=="Pending") pending.Add(delivery);
            }
            follow.NextDate=next;
            await repository.SaveAsync(follow,changes,ct); // Persist immutable payload and slot identity before contacting provider.
        }
        foreach(var delivery in pending.Where(x=>x.State=="Pending" && x.NextAttemptUtc<=Now)) {
            if((await registry.GetBySlugAsync(tenant.TenantId,ct))?.Status!=TenantRegistration.StatusActive) return;
            if(!recipients.Any(x=>x.Id==delivery.RecipientId && x.Email.Equals(delivery.RecipientEmail,StringComparison.OrdinalIgnoreCase))) {
                delivery.State="Skipped";delivery.Error="Recipient permissions or email changed.";await repository.SaveAsync(follow,[delivery],ct);continue;
            }
            if(delivery.FirstAttemptUtc.HasValue && Now-delivery.FirstAttemptUtc.Value>=TimeSpan.FromHours(20)) {
                delivery.State="Needs review";delivery.Error="Email acceptance is uncertain and the safe retry window expired. Check before creating a new reminder.";
                await repository.SaveAsync(follow,[delivery],ct);continue;
            }
            delivery.FirstAttemptUtc??=Now;delivery.Attempts++;delivery.UpdatedUtc=Now;
            await repository.SaveAsync(follow,[delivery],ct);
            try {
                await transport.SendAsync(delivery,ct);delivery.State="Sent";delivery.SentUtc=Now;delivery.Error=null;
            } catch(LeadReminderSendException failure) {
                delivery.Error=failure.Message;
                if(!failure.Retryable) delivery.State="Failed";
                else {
                    var delay=failure.RetryAfter??TimeSpan.FromMinutes(Math.Min(60,Math.Pow(2,Math.Min(delivery.Attempts,6))));
                    delay=delay<TimeSpan.FromSeconds(30)?TimeSpan.FromSeconds(30):delay;
                    delivery.NextAttemptUtc=Now.Add(delay>TimeSpan.FromHours(20)?TimeSpan.FromHours(20):delay);
                }
            }
            await repository.SaveAsync(follow,[delivery],ct);
        }
        var waiting=pending.Where(x=>x.State=="Pending").Select(x=>x.NextAttemptUtc).ToList();
        if(follow.NextDate.HasValue) waiting.Add(LeadRecurrence.DueUtc(follow.NextDate.Value,zone));
        follow.State=waiting.Count==0?"Completed":"Active";follow.NextCheckUtc=waiting.Count==0?DateTime.MaxValue:waiting.Min();
        follow.UpdatedUtc=Now;await repository.SaveAsync(follow,[],ct);
    }
    private LeadFollowUpDelivery Delivery(string leadName,LeadFollowUp follow,DateTime date,string zoneId,TimeZoneInfo zone,string recipientId,LeadAdminRecipient? recipient,string? skipped)
    {
        var identity=$"{follow.Id:N}/{follow.Revision:N}/{date:yyyy-MM-dd}/{recipientId}";
        var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0,16));
        var origin=config["App:FrontendBaseUrl"];
        if(!Uri.TryCreate(origin,UriKind.Absolute,out var uri) || uri.Scheme is not("https" or "http") || uri.AbsolutePath!="/")
            throw new LeadConflictException("The application link for follow-up emails is not configured.");
        var link=$"{uri.GetLeftPart(UriPartial.Authority)}/admin/leads/?tenant={Uri.EscapeDataString(tenant.TenantId)}&lead={follow.LeadId}";
        static string E(string value)=>WebUtility.HtmlEncode(value);
        return new LeadFollowUpDelivery { Id=id,LeadId=follow.LeadId,FollowUpId=follow.Id,Revision=follow.Revision,
            RecipientId=recipientId,RecipientName=recipient?.Name??"Unavailable admin",RecipientEmail=recipient?.Email??"",
            LocalDate=date,TimeZoneId=zoneId,DueUtc=LeadRecurrence.DueUtc(date,zone),NextAttemptUtc=Now,CreatedUtc=Now,UpdatedUtc=Now,
            State=skipped is null?"Pending":"Skipped",Error=skipped,Subject=$"ReadySetSiivous — Follow-up: {leadName}",
            Html=$"<div style=\"font-family:Arial,sans-serif;background:#f3f4f6;padding:24px\"><div style=\"background:white;max-width:600px;margin:auto;padding:28px;border-radius:12px\"><h1>Lead follow-up</h1><h2>{E(leadName)}</h2><p>Hello {E(recipient?.Name??"")},</p><p>Your lead follow-up is due on <strong>{date:dd/MM/yyyy} at 06:00 ({E(zoneId)})</strong>.</p><p style=\"white-space:pre-wrap\">{E(follow.Reason)}</p><p>Schedule: {E(follow.Recurrence)}{(follow.Recurrence=="Once"?"":$" · every {follow.Interval}")}.</p><a style=\"display:inline-block;background:#4f46e5;color:white;padding:12px 20px;border-radius:8px;text-decoration:none\" href=\"{E(link)}\">Open lead</a><p style=\"font-size:12px;color:#555\">Sign in with your tenant administrator account. This is an internal reminder; it has not been sent to the customer.</p></div></div>" };
    }
}
