using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Leads;
using CleaningSuite.Domain.Tenants;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace CleaningSuite.Tests.Leads;

public class LeadCollaborationTests
{
    [Fact] public void Month_end_recurrence_clamps_without_drifting()
    {
        var first=LeadRecurrence.Parse("2028-01-31");var feb=LeadRecurrence.Next(first,first,"Monthly",1)!.Value;
        Assert.Equal(new DateTime(2028,2,29),feb);Assert.Equal(new DateTime(2028,3,31),LeadRecurrence.Next(first,feb,"Monthly",1));
        Assert.Equal(new DateTime(2028,4,30),LeadRecurrence.Next(first,first,"Monthly",3));
        Assert.Null(LeadRecurrence.Next(first,first,"Once",1));Assert.Equal(first.AddDays(14),LeadRecurrence.Next(first,first,"Weekly",2));
    }
    [Fact] public void Six_am_follows_local_clock_across_dst()
    {
        var zone=TimeZoneConverter.TZConvert.GetTimeZoneInfo("Europe/Helsinki");
        var before=LeadRecurrence.DueUtc(new(2026,3,28),zone);var after=LeadRecurrence.DueUtc(new(2026,3,29),zone);
        Assert.Equal(4,before.Hour);Assert.Equal(3,after.Hour);Assert.Equal(TimeSpan.FromHours(23),after-before);
    }
    [Theory][InlineData("invalid")][InlineData("2026-02-30")][InlineData("9999-01-01")]
    public void Invalid_dates_are_validation_errors(string value)=>Assert.Throws<ValidationException>(()=>LeadRecurrence.Parse(value));
    [Fact] public async Task Recording_retry_preserves_server_author_and_rejects_reused_id_with_different_content()
    {
        var f=new Fixture();var id=Guid.NewGuid();var actor=new LeadActor("subject","Server name");
        var saved=await f.Service.SaveVoiceAsync(f.Lead.Id,id,[1,2],actor,default);
        Assert.Equal("Server name",saved.AuthorName);Assert.Equal(f.Clock.Utc.UtcDateTime,saved.CreatedUtc);
        Assert.Same(saved,await f.Service.SaveVoiceAsync(f.Lead.Id,id,[1,2],actor,default));
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.SaveVoiceAsync(f.Lead.Id,id,[3],actor,default));
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.SaveVoiceAsync(f.Lead.Id,id,[1,2],new("another","Other"),default));
        f.Audio.Verify(x=>x.SaveAsync(It.IsAny<LeadVoiceNote>(),It.IsAny<byte[]>(),default),Times.Once);
    }
    [Theory][InlineData("Lost",true,false)][InlineData("New",false,false)][InlineData("New",true,true)]
    public async Task Closed_inactive_and_converted_leads_cannot_receive_recordings(string status,bool active,bool converted)
    {
        var f=new Fixture();f.Lead.Fields=f.Lead.Fields with { Status=status };f.Lead.IsActive=active;if(converted) f.Lead.CompanyId=Guid.NewGuid();
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.SaveVoiceAsync(f.Lead.Id,Guid.NewGuid(),[1],new("a","Admin"),default));
        f.Audio.Verify(x=>x.SaveAsync(It.IsAny<LeadVoiceNote>(),It.IsAny<byte[]>(),default),Times.Never);
    }
    [Fact] public async Task Failed_recording_metadata_save_cleans_the_new_file()
    {
        var f=new Fixture();f.Repository.Setup(x=>x.SaveVoiceAsync(It.IsAny<LeadVoiceNote>(),default)).ThrowsAsync(new IOException());
        await Assert.ThrowsAsync<IOException>(()=>f.Service.SaveVoiceAsync(f.Lead.Id,Guid.NewGuid(),[1],new("a","Admin"),default));
        f.Audio.Verify(x=>x.DeleteAsync(It.IsAny<LeadVoiceNote>()),Times.Once);
    }
    [Fact] public async Task Follow_up_validates_dates_recipients_versions_and_cancellation()
    {
        var f=new Fixture();var input=new LeadFollowUpInput("Call back","2026-10-23",null,"Daily",1,["admin"]);
        var saved=await f.Service.SaveFollowUpAsync(f.Lead.Id,null,null,input,new("manager","Manager"),default);
        Assert.Equal("Manager",saved.AuthorName);Assert.Equal(3,saved.NextCheckUtc.Hour);
        await Assert.ThrowsAsync<ValidationException>(()=>f.Service.SaveFollowUpAsync(f.Lead.Id,null,null,input with { FirstDate="2026-10-22" },new("m","M"),default));
        await Assert.ThrowsAsync<ValidationException>(()=>f.Service.SaveFollowUpAsync(f.Lead.Id,null,null,input with { EndDate="2026-10-22" },new("m","M"),default));
        await Assert.ThrowsAsync<ValidationException>(()=>f.Service.SaveFollowUpAsync(f.Lead.Id,null,null,input with { RecipientIds=["outsider"] },new("m","M"),default));
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.SaveFollowUpAsync(f.Lead.Id,saved.Id,Guid.NewGuid(),input,new("m","M"),default));
        f.Deliveries.Add(new(){FollowUpId=saved.Id,LeadId=f.Lead.Id});
        await f.Service.CancelAsync(f.Lead.Id,saved.Id,saved.Version,default);
        Assert.Equal("Cancelled",saved.State);Assert.Equal("Cancelled",Assert.Single(f.Deliveries).State);
    }
    [Fact] public async Task Editing_reason_preserves_historical_anchor_and_blocks_uncertain_attempts()
    {
        var f=new Fixture();var follow=f.AddFollow("Monthly");follow.FirstDate=new(2026,8,31);follow.NextDate=new(2026,10,31);
        var input=new LeadFollowUpInput("New reason","2026-08-31",null,"Monthly",1,["admin"]);
        await f.Service.SaveFollowUpAsync(f.Lead.Id,follow.Id,follow.Version,input,new("m","Manager"),default);Assert.Equal(new DateTime(2026,10,31),follow.NextDate);Assert.Equal(new DateTime(2026,8,31),follow.FirstDate);
        f.Deliveries.Add(new(){LeadId=f.Lead.Id,FollowUpId=follow.Id,FirstAttemptUtc=f.Clock.Utc.UtcDateTime});
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.SaveFollowUpAsync(f.Lead.Id,follow.Id,follow.Version,input,new("m","Manager"),default));
    }
    [Theory][InlineData("Needs review",true)][InlineData("Pending",true)][InlineData("Sent",false)][InlineData("Failed",false)]
    public async Task Editing_checks_uncertain_history_without_locking_successful_notifications(string state,bool blocked)
    {
        var f=new Fixture();var follow=f.AddFollow("Daily");
        f.Deliveries.Add(new(){LeadId=f.Lead.Id,FollowUpId=follow.Id,State=state,FirstAttemptUtc=f.Clock.Utc.UtcDateTime});
        var input=new LeadFollowUpInput("New reason","2026-10-23",null,"Daily",1,["admin"]);
        if(blocked) await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.SaveFollowUpAsync(f.Lead.Id,follow.Id,follow.Version,input,new("m","Manager"),default));
        else await f.Service.SaveFollowUpAsync(f.Lead.Id,follow.Id,follow.Version,input,new("m","Manager"),default);
        Assert.Equal(state,f.Deliveries[0].State);
    }
    [Theory][InlineData("Completed")][InlineData("Cancelled")]
    public async Task Cancelling_terminal_schedules_preserves_their_history(string state)
    {
        var f=new Fixture();var follow=f.AddFollow("Once");follow.State=state;
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.CancelAsync(f.Lead.Id,follow.Id,follow.Version,default));
        Assert.Equal(state,follow.State);
    }
    [Fact] public async Task One_off_is_sent_once_and_retry_uses_immutable_payload()
    {
        var f=new Fixture();f.AddFollow("Once");await f.Dispatcher.DispatchAsync(f.Lead.Id,f.Follows[0].Id,default);
        await f.Dispatcher.DispatchAsync(f.Lead.Id,f.Follows[0].Id,default);
        var sent=Assert.Single(f.Deliveries);Assert.Equal("Sent",sent.State);Assert.Equal("Completed",f.Follows[0].State);
        Assert.Contains("Prospect",sent.Html);Assert.Contains("&lt;reason&gt;",sent.Html);Assert.Contains("tenant=tenant-a",sent.Html);
        f.Transport.Verify(x=>x.SendAsync(It.IsAny<LeadFollowUpDelivery>(),default),Times.Once);
    }
    [Fact] public async Task Partial_failure_retries_only_failed_recipient_and_ends_uncertain_window()
    {
        var f=new Fixture();f.Admins.Add(new("second","Second","second@example.test"));var follow=f.AddFollow("Once");follow.RecipientIds.Add("second");
        f.Transport.Setup(x=>x.SendAsync(It.Is<LeadFollowUpDelivery>(d=>d.RecipientId=="admin"),default)).ThrowsAsync(new LeadReminderSendException("Temporary",true));
        await f.Dispatcher.DispatchAsync(f.Lead.Id,follow.Id,default);Assert.Single(f.Deliveries.Where(x=>x.State=="Sent"));Assert.Single(f.Deliveries.Where(x=>x.State=="Pending"));
        f.Clock.Utc=f.Clock.Utc.AddMinutes(3);await f.Dispatcher.DispatchAsync(f.Lead.Id,follow.Id,default);
        f.Transport.Verify(x=>x.SendAsync(It.Is<LeadFollowUpDelivery>(d=>d.RecipientId=="second"),default),Times.Once);
        f.Clock.Utc=f.Clock.Utc.AddHours(21);await f.Dispatcher.DispatchAsync(f.Lead.Id,follow.Id,default);
        Assert.Equal("Needs review",f.Deliveries.Single(x=>x.RecipientId=="admin").State);
    }
    [Fact] public async Task Directory_outage_defers_instead_of_sending_or_skipping()
    {
        var f=new Fixture();var follow=f.AddFollow("Once");f.Directory.Setup(x=>x.ListAsync(default)).ThrowsAsync(new LeadConflictException("Unavailable"));
        await f.Dispatcher.DispatchAsync(f.Lead.Id,follow.Id,default);Assert.Empty(f.Deliveries);Assert.Equal("Unavailable",follow.LastError);Assert.Equal(f.Clock.Utc.UtcDateTime.AddMinutes(5),follow.NextCheckUtc);
        f.Transport.Verify(x=>x.SendAsync(It.IsAny<LeadFollowUpDelivery>(),default),Times.Never);
    }
    [Fact] public async Task Downtime_coalesces_recurring_notifications_and_expired_end_date_skips()
    {
        var f=new Fixture();var follow=f.AddFollow("Daily");follow.FirstDate=new DateTime(2026,10,1);follow.NextDate=follow.FirstDate;
        await f.Dispatcher.DispatchAsync(f.Lead.Id,follow.Id,default);
        Assert.Single(f.Deliveries.Where(x=>x.State=="Sent"));Assert.Single(f.Deliveries.Where(x=>x.State=="Skipped"));
        Assert.Equal(new DateTime(2026,10,24),follow.NextDate);
        var another=f.AddFollow("Daily");another.EndDate=new(2026,10,22);await f.Dispatcher.DispatchAsync(f.Lead.Id,another.Id,default);Assert.Equal("Completed",another.State);
    }
    [Fact] public async Task Suspended_tenant_and_ineligible_recipient_are_not_sent()
    {
        var f=new Fixture();var follow=f.AddFollow("Once");f.Registration.Status=TenantRegistration.StatusSuspended;
        await f.Dispatcher.DispatchAsync(f.Lead.Id,follow.Id,default);Assert.Empty(f.Deliveries);
        f.Registration.Status=TenantRegistration.StatusActive;f.Admins.Clear();await f.Dispatcher.DispatchAsync(f.Lead.Id,follow.Id,default);
        Assert.Equal("Skipped",Assert.Single(f.Deliveries).State);f.Transport.Verify(x=>x.SendAsync(It.IsAny<LeadFollowUpDelivery>(),default),Times.Never);
    }
    public sealed class Clock : TimeProvider { public DateTimeOffset Utc=new(2026,10,23,4,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Utc; }
    public sealed class Fixture
    {
        public Lead Lead=new(){Version=Guid.NewGuid(),Fields=new(){Name="Prospect"}};
        public Clock Clock=new();public List<LeadVoiceNote> Voices=[];public List<LeadFollowUp> Follows=[];public List<LeadFollowUpDelivery> Deliveries=[];
        public List<LeadAdminRecipient> Admins=[new("admin","Admin","admin@example.test")];public TenantRegistration Registration=new(){Slug="tenant-a",Status=TenantRegistration.StatusActive};
        public Mock<ILeadRepository> Leads=new();public Mock<ILeadCollaborationRepository> Repository=new();public Mock<ILeadAudioStore> Audio=new();
        public Mock<ILeadAudioProbe> Probe=new();public Mock<ILeadAdminDirectory> Directory=new();public Mock<ILeadReminderTransport> Transport=new();
        public Mock<ITenantProfileRepository> Profiles=new();public Mock<ITenantContext> Tenant=new();public Mock<ITenantRegistry> Registry=new();
        public LeadCollaborationService Service;public LeadFollowUpDispatcher Dispatcher;
        public Fixture()
        {
            Tenant.SetupGet(x=>x.TenantId).Returns("tenant-a");Profiles.Setup(x=>x.GetAsync("tenant-a",default)).ReturnsAsync(new TenantProfile{TimeZoneId="Europe/Helsinki"});
            Leads.Setup(x=>x.GetAsync(Lead.Id,default)).ReturnsAsync(Lead);Leads.Setup(x=>x.LockAsync(Lead.Id,default)).ReturnsAsync(Mock.Of<IAsyncDisposable>());
            Probe.Setup(x=>x.ValidateAsync(It.IsAny<byte[]>(),default)).ReturnsAsync(new LeadAudioInfo(".webm","audio/webm",2));
            Directory.Setup(x=>x.ListAsync(default)).ReturnsAsync(()=>Admins);Registry.Setup(x=>x.GetBySlugAsync("tenant-a",default)).ReturnsAsync(()=>Registration);
            Repository.Setup(x=>x.VoiceAsync(Lead.Id,It.IsAny<Guid>(),default)).ReturnsAsync((Guid l,Guid id,CancellationToken ct)=>Voices.SingleOrDefault(x=>x.Id==id));
            Repository.Setup(x=>x.SaveVoiceAsync(It.IsAny<LeadVoiceNote>(),default)).Callback((LeadVoiceNote note,CancellationToken ct)=>{note.Version=Guid.NewGuid();Voices.Add(note);}).Returns(Task.CompletedTask);
            Repository.Setup(x=>x.FollowUpAsync(Lead.Id,It.IsAny<Guid>(),default)).ReturnsAsync((Guid l,Guid id,CancellationToken ct)=>Follows.SingleOrDefault(x=>x.Id==id));
            Repository.Setup(x=>x.HasUncertainDeliveryAsync(It.IsAny<Guid>(),default)).ReturnsAsync((Guid id,CancellationToken ct)=>Deliveries.Any(x=>x.FollowUpId==id && x.FirstAttemptUtc.HasValue && (x.State=="Pending" || x.State=="Needs review")));
            Repository.Setup(x=>x.PendingAsync(It.IsAny<Guid>(),default)).ReturnsAsync((Guid id,CancellationToken ct)=>(IReadOnlyList<LeadFollowUpDelivery>)Deliveries.Where(x=>x.FollowUpId==id && x.State=="Pending").ToArray());
            Repository.Setup(x=>x.SaveAsync(It.IsAny<LeadFollowUp>(),It.IsAny<IEnumerable<LeadFollowUpDelivery>>(),default)).Callback((LeadFollowUp follow,IEnumerable<LeadFollowUpDelivery> deliveries,CancellationToken ct)=>{follow.Version=Guid.NewGuid();if(!Follows.Contains(follow)) Follows.Add(follow);foreach(var d in deliveries) if(!Deliveries.Any(x=>x.Id==d.Id)) Deliveries.Add(d);}).Returns(Task.CompletedTask);
            Service=new(Leads.Object,Repository.Object,Audio.Object,Probe.Object,Directory.Object,Profiles.Object,Tenant.Object,Clock);
            Dispatcher=new(Leads.Object,Repository.Object,Directory.Object,Transport.Object,Profiles.Object,Tenant.Object,Clock,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"App:FrontendBaseUrl","https://app.test"}}).Build(),Registry.Object,NullLogger<LeadFollowUpDispatcher>.Instance);
        }
        public LeadFollowUp AddFollow(string recurrence) { var follow=new LeadFollowUp{LeadId=Lead.Id,Version=Guid.NewGuid(),FirstDate=new(2026,10,23),NextDate=new(2026,10,23),NextCheckUtc=Clock.Utc.UtcDateTime,Reason="<reason>",Recurrence=recurrence,RecipientIds=["admin"]};Follows.Add(follow);return follow; }
    }
}
