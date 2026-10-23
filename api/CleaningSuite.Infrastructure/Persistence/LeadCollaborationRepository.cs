using CleaningSuite.Application.Common;
using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using Marten;
namespace CleaningSuite.Infrastructure.Persistence;

public sealed class LeadCollaborationRepository(ITenantSession tenant) : ILeadCollaborationRepository
{
    private IDocumentSession Session=>tenant.Session;
    public async Task<LeadVoiceNote?> VoiceAsync(Guid leadId,Guid id,CancellationToken ct)
    { var note=await Session.LoadAsync<LeadVoiceNote>(id,ct);return note is not null && note.LeadId!=leadId?throw new NotFoundException("Voice note",id):note; }
    public async Task<(IReadOnlyList<LeadVoiceNote> Items,int Total)> VoicesAsync(Guid leadId,int skip,int take,CancellationToken ct)
    { var query=Session.Query<LeadVoiceNote>().Where(x=>x.LeadId==leadId);var total=await query.CountAsync(ct);return(await query.OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Skip(skip).Take(take).ToListAsync(ct),total); }
    public async Task SaveVoiceAsync(LeadVoiceNote note,CancellationToken ct) { Session.Store(note);await Session.SaveChangesAsync(ct); }
    public async Task DeleteVoiceAsync(LeadVoiceNote note,CancellationToken ct) { Session.Delete(note);await Session.SaveChangesAsync(ct); }
    public async Task<LeadFollowUp?> FollowUpAsync(Guid leadId,Guid id,CancellationToken ct)
    { var follow=await Session.LoadAsync<LeadFollowUp>(id,ct);return follow?.LeadId==leadId?follow:null; }
    public async Task<(IReadOnlyList<LeadFollowUp> Items,int Total)> FollowUpsAsync(Guid leadId,int skip,int take,CancellationToken ct)
    { var query=Session.Query<LeadFollowUp>().Where(x=>x.LeadId==leadId);var total=await query.CountAsync(ct);return(await query.OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Skip(skip).Take(take).ToListAsync(ct),total); }
    public Task<bool> HasUncertainDeliveryAsync(Guid id,CancellationToken ct)=>
        Session.Query<LeadFollowUpDelivery>().AnyAsync(x=>x.FollowUpId==id && x.FirstAttemptUtc!=null && (x.State=="Pending" || x.State=="Needs review"),ct);
    public async Task<IReadOnlyList<LeadFollowUpDelivery>> PendingAsync(Guid id,CancellationToken ct)=>
        await Session.Query<LeadFollowUpDelivery>().Where(x=>x.FollowUpId==id && x.State=="Pending").ToListAsync(ct);
    public async Task<(IReadOnlyList<LeadFollowUpDelivery> Items,int Total)> HistoryAsync(Guid leadId,int skip,int take,CancellationToken ct)
    { var query=Session.Query<LeadFollowUpDelivery>().Where(x=>x.LeadId==leadId);var total=await query.CountAsync(ct);return(await query.OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Skip(skip).Take(take).ToListAsync(ct),total); }
    public async Task SaveAsync(LeadFollowUp follow,IEnumerable<LeadFollowUpDelivery> deliveries,CancellationToken ct)
    { Session.Store(follow);foreach(var delivery in deliveries) Session.Store(delivery);await Session.SaveChangesAsync(ct); }
    public async Task<IReadOnlyList<(Guid LeadId,Guid Id)>> DueAsync(DateTime now,CancellationToken ct)
    { var databaseUtc=DateTime.SpecifyKind(now,DateTimeKind.Unspecified);var due=await Session.Query<LeadFollowUp>().Where(x=>x.State=="Active" && x.NextCheckUtc<=databaseUtc).OrderBy(x=>x.NextCheckUtc).ThenBy(x=>x.Id).Take(100).ToListAsync(ct);return due.Select(x=>(x.LeadId,x.Id)).ToArray(); }

    // Called before the existing lead transaction commits, under the same lead reservation.
    public static async Task CancelForLeadAsync(IDocumentSession session,Lead lead,bool deleting,CancellationToken ct)
    {
        if(!deleting && lead.IsActive && !lead.CompanyId.HasValue && lead.Fields.Status!="Lost") return;
        var follows=await session.Query<LeadFollowUp>().Where(x=>x.LeadId==lead.Id).ToListAsync(ct);
        var deliveries=await session.Query<LeadFollowUpDelivery>().Where(x=>x.LeadId==lead.Id).ToListAsync(ct);
        foreach(var follow in follows) {
            if(deleting) session.Delete(follow);
            else if(follow.State=="Active") { follow.State="Cancelled";follow.NextDate=null;follow.UpdatedUtc=lead.UpdatedUtc;session.Store(follow); }
        }
        foreach(var delivery in deliveries) {
            if(deleting) session.Delete(delivery);
            else if(delivery.State=="Pending") { delivery.State="Cancelled";delivery.Error="Lead closed, inactive or converted.";session.Store(delivery); }
        }
        if(deleting) foreach(var note in await session.Query<LeadVoiceNote>().Where(x=>x.LeadId==lead.Id).ToListAsync(ct)) session.Delete(note);
    }
}
