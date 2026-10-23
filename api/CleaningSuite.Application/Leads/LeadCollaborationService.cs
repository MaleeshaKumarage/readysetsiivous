using System.Security.Cryptography;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Leads;
using FluentValidation;
namespace CleaningSuite.Application.Leads;

public sealed class LeadCollaborationService(ILeadRepository leads, ILeadCollaborationRepository repository,
    ILeadAudioStore audio, ILeadAudioProbe probe, ILeadAdminDirectory directory,
    ITenantProfileRepository profiles, ITenantContext tenant, TimeProvider clock)
{
    public static void Page(int skip, int take) { if (skip < 0 || take is < 1 or > 100) throw new ValidationException("Choose a page size between 1 and 100."); }
    private async Task<Lead> Require(Guid id, CancellationToken ct) => await leads.GetAsync(id, ct) ?? throw new NotFoundException("Lead", id);
    public static void Active(Lead lead)
    {
        if (!lead.IsActive || lead.CompanyId.HasValue || lead.Fields.Status == "Lost")
            throw new LeadConflictException("This lead is closed, inactive or converted. Existing history is retained; new notes and follow-ups are disabled.");
    }
    public async Task<Paged<LeadVoiceNote>> VoicesAsync(Guid id, int skip, int take, CancellationToken ct)
    { Page(skip,take); await Require(id,ct); var page=await repository.VoicesAsync(id,skip,take,ct); return new(page.Items,page.Total); }
    public async Task<LeadVoiceNote> SaveVoiceAsync(Guid id, Guid recordingId, byte[] bytes, LeadActor actor, CancellationToken ct)
    {
        if (recordingId == Guid.Empty || bytes.Length is 0 or > 20 * 1024 * 1024) throw new ValidationException("Choose a non-empty recording up to 20 MiB.");
        await Require(id,ct);
        var hash=Convert.ToHexString(SHA256.HashData(bytes));
        var info=await probe.ValidateAsync(bytes,ct);
        await using var reservation=await leads.LockAsync(id,ct);
        var lead=await Require(id,ct);
        var existing=await repository.VoiceAsync(id,recordingId,ct);
        if(existing is not null) {
            if(existing.AuthorId!=actor.Id || existing.ContentHash!=hash) throw new LeadConflictException("This recording ID has already been used for a different recording.");
            return existing;
        }
        Active(lead);
        var note=new LeadVoiceNote { Id=recordingId,LeadId=id,AuthorId=actor.Id,AuthorName=actor.Name,
            ContentHash=hash,Extension=info.Extension,ContentType=info.ContentType,DurationSeconds=info.DurationSeconds,
            CreatedUtc=clock.GetUtcNow().UtcDateTime,UpdatedUtc=clock.GetUtcNow().UtcDateTime };
        await audio.SaveAsync(note,bytes,ct);
        try { await repository.SaveVoiceAsync(note,ct); }
        catch { await audio.DeleteAsync(note); throw; }
        return note;
    }
    public async Task<(byte[] Bytes,string ContentType)> ReadVoiceAsync(Guid id,Guid noteId,CancellationToken ct)
    {
        await Require(id,ct); var note=await repository.VoiceAsync(id,noteId,ct) ?? throw new NotFoundException("Voice note",noteId);
        return (await audio.ReadAsync(note,ct) ?? throw new NotFoundException("Voice note",noteId),note.ContentType);
    }
    public async Task DeleteVoiceAsync(Guid id,Guid noteId,Guid version,CancellationToken ct)
    {
        await using var reservation=await leads.LockAsync(id,ct); var lead=await Require(id,ct);
        if(lead.CompanyId.HasValue) throw new LeadConflictException("Converted lead history is read-only.");
        var note=await repository.VoiceAsync(id,noteId,ct) ?? throw new NotFoundException("Voice note",noteId);
        if(version==Guid.Empty || note.Version!=version) throw new LeadConflictException("This voice note changed. Refresh before deleting.");
        await repository.DeleteVoiceAsync(note,ct); await audio.DeleteAsync(note);
    }
    public async Task<Paged<LeadFollowUp>> FollowUpsAsync(Guid id,int skip,int take,CancellationToken ct)
    { Page(skip,take); await Require(id,ct); var page=await repository.FollowUpsAsync(id,skip,take,ct);return new(page.Items,page.Total); }
    public async Task<Paged<LeadFollowUpDelivery>> HistoryAsync(Guid id,int skip,int take,CancellationToken ct)
    { Page(skip,take); await Require(id,ct); var page=await repository.HistoryAsync(id,skip,take,ct);return new(page.Items,page.Total); }
    public async Task<LeadFollowUp> SaveFollowUpAsync(Guid leadId,Guid? id,Guid? version,LeadFollowUpInput input,LeadActor actor,CancellationToken ct)
    {
        await Require(leadId,ct);
        var (_,zone)=await TenantTimeZone.LoadAsync(profiles,tenant,ct);
        var first=LeadRecurrence.Parse(input.FirstDate); var end=string.IsNullOrWhiteSpace(input.EndDate)?(DateTime?)null:LeadRecurrence.Parse(input.EndDate);
        var today=TenantTimeZone.LocalDate(clock.GetUtcNow().UtcDateTime,zone);
        var reason=input.Reason?.Trim()??""; var recipients=input.RecipientIds?.Distinct(StringComparer.Ordinal).ToList()??[];
        if(reason.Length is <1 or >1000 || end<first || end<today || input.Interval is <1 or >365
            || !new[]{"Once","Daily","Weekly","Monthly"}.Contains(input.Recurrence) || recipients.Count is <1 or >20
            || recipients.Any(x=>string.IsNullOrWhiteSpace(x) || x.Length>200))
            throw new ValidationException("Enter a reason (1–1,000 characters), today or a future date, an ordered end date, recurrence interval 1–365, and 1–20 tenant admins.");
        var eligible=await directory.ListAsync(ct);
        if(recipients.Any(x=>!eligible.Any(a=>a.Id==x))) throw new ValidationException("One or more selected recipients are no longer eligible tenant admins. Refresh the recipients.");
        await using var reservation=await leads.LockAsync(leadId,ct); Active(await Require(leadId,ct));
        var follow=id.HasValue?await repository.FollowUpAsync(leadId,id.Value,ct)??throw new NotFoundException("Follow-up",id.Value):new LeadFollowUp{LeadId=leadId};
        if(id.HasValue && (version==Guid.Empty || version!=follow.Version || follow.State!="Active")) throw new LeadConflictException("This follow-up changed or is no longer active. Refresh before editing.");
        var preserveAnchor=id.HasValue && first==follow.FirstDate && input.Recurrence==follow.Recurrence && input.Interval==follow.Interval;
        if(first<today && !preserveAnchor) throw new ValidationException("Choose today or a future first date when creating or changing the schedule.");
        var nextDate=preserveAnchor?follow.NextDate??first:first;
        var pending=id.HasValue?await repository.PendingAsync(follow.Id,ct):[];
        if(id.HasValue && await repository.HasUncertainDeliveryAsync(follow.Id,ct)) throw new LeadConflictException("A notification is awaiting email acceptance confirmation. Wait for its outcome, or cancel and review its history before creating a new reminder.");
        foreach(var delivery in pending) { delivery.State="Cancelled";delivery.Error="Schedule edited."; }
        follow.Reason=reason;follow.FirstDate=first;follow.EndDate=end;follow.Recurrence=input.Recurrence;follow.Interval=input.Interval;
        follow.RecipientIds=recipients;follow.AuthorId=actor.Id;follow.AuthorName=actor.Name;follow.Revision=Guid.NewGuid();
        follow.NextDate=nextDate>end?null:nextDate;follow.NextCheckUtc=follow.NextDate.HasValue?LeadRecurrence.DueUtc(follow.NextDate.Value,zone):clock.GetUtcNow().UtcDateTime;follow.State="Active";follow.LastError=null;follow.UpdatedUtc=clock.GetUtcNow().UtcDateTime;
        if(!id.HasValue) follow.CreatedUtc=follow.UpdatedUtc;
        await repository.SaveAsync(follow,pending,ct);return follow;
    }
    public async Task CancelAsync(Guid leadId,Guid id,Guid version,CancellationToken ct)
    {
        await using var reservation=await leads.LockAsync(leadId,ct);var lead=await Require(leadId,ct);
        if(lead.CompanyId.HasValue) throw new LeadConflictException("Converted lead history is read-only.");
        var follow=await repository.FollowUpAsync(leadId,id,ct)??throw new NotFoundException("Follow-up",id);
        if(version==Guid.Empty || follow.Version!=version || follow.State!="Active") throw new LeadConflictException("This follow-up changed or is no longer active. Refresh before cancelling.");
        follow.State="Cancelled";follow.NextDate=null;follow.UpdatedUtc=clock.GetUtcNow().UtcDateTime;
        var pending=await repository.PendingAsync(id,ct);foreach(var delivery in pending) { delivery.State="Cancelled";delivery.Error="Follow-up cancelled."; }
        await repository.SaveAsync(follow,pending,ct);
    }
    public async Task CleanupAsync(CancellationToken ct)
    {
        foreach(var candidate in await audio.UnreferencedCandidatesAsync(clock.GetUtcNow().UtcDateTime.AddHours(-24),ct)) {
            await using var reservation=await leads.LockAsync(candidate.LeadId,ct);
            if(await repository.VoiceAsync(candidate.LeadId,candidate.NoteId,ct) is null)
                await audio.DeleteAsync(new LeadVoiceNote{Id=candidate.NoteId,LeadId=candidate.LeadId,Extension=candidate.Extension});
        }
    }
}
