using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace CleaningSuite.Tests.Partners;

public class PartnerNotificationDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Cleaning_updates_are_app_only_when_partner_email_is_off()
    {
        using var f = await Fixture.Create(emailEnabled: false); using var session = f.Session();
        f.Transport.Setup(t => t.SendAsync(It.IsAny<PartnerNotificationDelivery>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await f.Dispatcher(session).DispatchAsync(CancellationToken.None);
        using var query = f.Store.QuerySession(f.TenantId);
        var stored = (await query.LoadAsync<PartnerNotification>(f.Notification.Id))!;
        Assert.Equal("Skipped", stored.State);
        Assert.Empty(stored.Deliveries);
        f.Transport.Verify(t => t.SendAsync(It.IsAny<PartnerNotificationDelivery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [QualityCycleDatabaseFact]
    public async Task Worker_poll_with_UTC_clock_dispatches_due_notifications_but_not_future_notifications()
    {
        using var f = await Fixture.Create(); using var session = f.Session();
        var future = new PartnerNotification { CompanyId = f.Company.Id, Audience = "Company", Kind = "OfferAccepted", NextAttemptUtc = f.Now.UtcDateTime.AddHours(1) };
        using (var seed = f.Store.LightweightSession(f.TenantId)) { seed.Store(future); await seed.SaveChangesAsync(); }
        f.Transport.Setup(t => t.SendAsync(It.IsAny<PartnerNotificationDelivery>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        // Exercise the worker's actual poll, not just the per-ID dispatch overload.
        await f.Dispatcher(session).DispatchAsync(CancellationToken.None);
        using var query = f.Store.QuerySession(f.TenantId);
        Assert.Equal("Sent", (await query.LoadAsync<PartnerNotification>(f.Notification.Id))!.State);
        Assert.Equal("Pending", (await query.LoadAsync<PartnerNotification>(future.Id))!.State);
        f.Transport.Verify(t => t.SendAsync(It.IsAny<PartnerNotificationDelivery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [QualityCycleDatabaseFact]
    public async Task Slow_provider_does_not_reserve_tenant_scheduling_or_allow_duplicate_dispatch()
    {
        using var f = await Fixture.Create(); using var sender = f.Session(); using var duplicate = f.Session(); using var scheduler = f.Session();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.Setup(t => t.SendAsync(It.IsAny<PartnerNotificationDelivery>(), It.IsAny<CancellationToken>()))
            .Returns<PartnerNotificationDelivery, CancellationToken>(async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); });
        var send = f.Dispatcher(sender).DispatchAsync(f.Notification.Id, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // The provider remains paused. Starting work, acceptance and invoice issue use this
            // reservation and must remain available independently of email response time.
            await using var work = await new ShiftWorkReservation(f.Config, scheduler).AcquireAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await f.Dispatcher(duplicate).DispatchAsync(f.Notification.Id, default).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(send.IsCompleted);
            f.Transport.Verify(t => t.SendAsync(It.IsAny<PartnerNotificationDelivery>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { release.TrySetResult(); await send; }
        using var query = f.Store.QuerySession(f.TenantId);
        Assert.Equal("Sent", (await query.LoadAsync<PartnerNotification>(f.Notification.Id))!.State);
    }

    [QualityCycleDatabaseFact]
    public async Task Recipient_changed_during_another_provider_send_is_rechecked_before_its_attempt()
    {
        using var f = await Fixture.Create(); using var sender = f.Session();
        using (var edit = f.Store.LightweightSession(f.TenantId))
        {
            var item = (await edit.LoadAsync<PartnerNotification>(f.Notification.Id))!; item.Audience = "Staff"; edit.Store(item);
            edit.Store(new Employee { Role = Employee.RoleAdmin, Email = "first@example.test" }, new Employee { Role = Employee.RoleManager, Email = "second@example.test" });
            await edit.SaveChangesAsync();
        }
        f.Transport.Setup(t => t.SendAsync(It.IsAny<PartnerNotificationDelivery>(), It.IsAny<CancellationToken>()))
            .Returns<PartnerNotificationDelivery, CancellationToken>(async (delivery, ct) =>
            {
                using var edit = f.Store.LightweightSession(f.TenantId);
                var item = (await edit.LoadAsync<PartnerNotification>(f.Notification.Id, ct))!;
                var other = item.Deliveries.Single(d => d.Id != delivery.Id);
                var employee = (await edit.LoadAsync<Employee>(other.RecipientEmployeeId, ct))!;
                employee.IsActive = false; edit.Store(employee); await edit.SaveChangesAsync(ct);
            });
        await f.Dispatcher(sender).DispatchAsync(f.Notification.Id, default);
        using var query = f.Store.QuerySession(f.TenantId);
        var stored = (await query.LoadAsync<PartnerNotification>(f.Notification.Id))!;
        Assert.Single(stored.Deliveries, d => d.SentUtc.HasValue);
        Assert.Single(stored.Deliveries, d => d.SkippedUtc.HasValue);
        f.Transport.Verify(t => t.SendAsync(It.IsAny<PartnerNotificationDelivery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [QualityCycleDatabaseFact]
    public async Task Concurrent_dispatch_sends_one_delivery_and_persists_immutable_payload_before_provider_call()
    {
        using var f=await Fixture.Create(); using var first=f.Session();using var second=f.Session();
        f.Transport.Setup(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>())).Returns<PartnerNotificationDelivery,CancellationToken>(async(delivery,ct)=>
        {
            using var check=f.Store.QuerySession(f.TenantId);
            var stored=await check.LoadAsync<PartnerNotification>(f.Notification.Id,ct);
            Assert.NotNull(stored!.FirstAttemptUtc);Assert.Equal(delivery.Html,Assert.Single(stored.Deliveries).Html);
            Assert.Contains("/partner/",delivery.Html);Assert.DoesNotContain("private access code",delivery.Html);
        });
        await Task.WhenAll(f.Dispatcher(first).DispatchAsync(f.Notification.Id,default),f.Dispatcher(second).DispatchAsync(f.Notification.Id,default));
        f.Transport.Verify(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>()),Times.Once);
        using var final=f.Store.QuerySession(f.TenantId);Assert.Equal("Sent",(await final.LoadAsync<PartnerNotification>(f.Notification.Id))!.State);
        using var foreign=f.Store.QuerySession(f.TenantId+"-other");Assert.Null(await foreign.LoadAsync<PartnerNotification>(f.Notification.Id));
    }
    [QualityCycleDatabaseFact]
    public async Task Transient_failure_retries_same_snapshot_and_id_then_stops_at_safe_window()
    {
        using var f=await Fixture.Create();using var session=f.Session();
        f.Transport.Setup(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>())).ThrowsAsync(new PartnerNotificationSendException("uncertain",true,TimeSpan.FromSeconds(30)));
        var dispatcher=f.Dispatcher(session);await dispatcher.DispatchAsync(f.Notification.Id,default);
        PartnerNotification first;using(var q=f.Store.QuerySession(f.TenantId))first=(await q.LoadAsync<PartnerNotification>(f.Notification.Id))!;
        Assert.Equal("Pending",first.State);Assert.Equal(1,first.Attempts);
        f.Now=f.Now.AddMinutes(1);await dispatcher.DispatchAsync(f.Notification.Id,default);
        using(var q=f.Store.QuerySession(f.TenantId))
        { var next=(await q.LoadAsync<PartnerNotification>(f.Notification.Id))!;Assert.Equal(2,next.Attempts);Assert.Equal(first.Deliveries[0].Id,next.Deliveries[0].Id);Assert.Equal(first.Deliveries[0].Html,next.Deliveries[0].Html); }
        f.Now=f.Now.AddHours(21);await dispatcher.DispatchAsync(f.Notification.Id,default);
        using(var q=f.Store.QuerySession(f.TenantId))Assert.Equal("NeedsReview",(await q.LoadAsync<PartnerNotification>(f.Notification.Id))!.State);
        f.Transport.Verify(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>()),Times.Exactly(2));
    }
    [QualityCycleDatabaseFact]
    public async Task Recipient_rebinding_after_failure_skips_instead_of_retargeting_same_key()
    {
        using var f=await Fixture.Create();using var session=f.Session();
        f.Transport.Setup(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>())).ThrowsAsync(new PartnerNotificationSendException("uncertain",true,TimeSpan.FromSeconds(30)));
        var dispatcher=f.Dispatcher(session);await dispatcher.DispatchAsync(f.Notification.Id,default);
        using(var edit=f.Store.LightweightSession(f.TenantId)) { var employee=(await edit.LoadAsync<Employee>(f.Owner.Id))!;employee.PartnerCompanyId=Guid.NewGuid();edit.Store(employee);await edit.SaveChangesAsync(); }
        f.Now=f.Now.AddMinutes(1);await dispatcher.DispatchAsync(f.Notification.Id,default);
        using var q=f.Store.QuerySession(f.TenantId);Assert.Equal("Skipped",(await q.LoadAsync<PartnerNotification>(f.Notification.Id))!.State);
        f.Transport.Verify(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>()),Times.Once);
    }
    [QualityCycleDatabaseFact]
    public async Task Ineligible_or_permanently_rejected_recipient_does_not_block_other_recipients()
    {
        using var f=await Fixture.Create();using var session=f.Session();
        using(var edit=f.Store.LightweightSession(f.TenantId))
        {
            var item=(await edit.LoadAsync<PartnerNotification>(f.Notification.Id))!;item.Audience="Staff";edit.Store(item);
            edit.Store(new Employee{Role=Employee.RoleAdmin,Email="first@example.test"},new Employee{Role=Employee.RoleManager,Email="second@example.test"});await edit.SaveChangesAsync();
        }
        f.Transport.SetupSequence(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PartnerNotificationSendException("Recipient rejected",false)).Returns(Task.CompletedTask);
        await f.Dispatcher(session).DispatchAsync(f.Notification.Id,default);
        using var q=f.Store.QuerySession(f.TenantId);var stored=(await q.LoadAsync<PartnerNotification>(f.Notification.Id))!;
        Assert.Single(stored.Deliveries,d=>d.FailedUtc.HasValue);Assert.Single(stored.Deliveries,d=>d.SentUtc.HasValue);
        Assert.Equal("Failed",stored.State);f.Transport.Verify(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>()),Times.Exactly(2));
    }
    [QualityCycleDatabaseFact]
    public async Task Revoked_staff_recipient_does_not_block_other_pending_recipients()
    {
        using var f=await Fixture.Create();using var session=f.Session();
        using(var edit=f.Store.LightweightSession(f.TenantId))
        {
            var item=(await edit.LoadAsync<PartnerNotification>(f.Notification.Id))!;item.Audience="Staff";edit.Store(item);
            edit.Store(new Employee{Role=Employee.RoleAdmin,Email="first@example.test"},new Employee{Role=Employee.RoleManager,Email="second@example.test"});await edit.SaveChangesAsync();
        }
        f.Transport.SetupSequence(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PartnerNotificationSendException("uncertain",true,TimeSpan.FromSeconds(30))).Returns(Task.CompletedTask);
        var dispatcher=f.Dispatcher(session);await dispatcher.DispatchAsync(f.Notification.Id,default);
        using(var edit=f.Store.LightweightSession(f.TenantId))
        {
            var item=(await edit.LoadAsync<PartnerNotification>(f.Notification.Id))!;
            var employee=(await edit.LoadAsync<Employee>(item.Deliveries[0].RecipientEmployeeId))!;employee.IsActive=false;edit.Store(employee);await edit.SaveChangesAsync();
        }
        f.Now=f.Now.AddMinutes(1);await dispatcher.DispatchAsync(f.Notification.Id,default);
        using var q=f.Store.QuerySession(f.TenantId);var stored=(await q.LoadAsync<PartnerNotification>(f.Notification.Id))!;
        Assert.Single(stored.Deliveries,d=>d.SkippedUtc.HasValue);Assert.Single(stored.Deliveries,d=>d.SentUtc.HasValue);
        Assert.Equal("Sent",stored.State);f.Transport.Verify(t=>t.SendAsync(It.IsAny<PartnerNotificationDelivery>(),It.IsAny<CancellationToken>()),Times.Exactly(2));
    }
    private sealed class Fixture:IDisposable
    {
        public required ServiceProvider Provider;public required IDocumentStore Store;public required IConfiguration Config;
        public string TenantId="partner-delivery-"+Guid.NewGuid().ToString("N");public Company Company=Company.Create("1234567-8","Synthetic owner");
        public Employee Owner=new(){Email="owner@example.test",FirstName="Owner",Role=Employee.RolePartner};
        public PartnerNotification Notification=new(){Audience="Company",Kind="OfferAccepted"};public Mock<IPartnerNotificationTransport> Transport=new();
        public DateTimeOffset Now=new(2026,11,1,12,0,0,TimeSpan.Zero);
        public static async Task<Fixture> Create(bool emailEnabled = true)
        {
            var connection=Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
            var services=new ServiceCollection();services.AddLogging();services.AddCleaningMarten(connection,databaseSchemaName:"partner_delivery_"+Guid.NewGuid().ToString("N"));
            var provider=services.BuildServiceProvider();var store=provider.GetRequiredService<IDocumentStore>();await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            var f=new Fixture{Provider=provider,Store=store,Config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",connection},{"App:FrontendBaseUrl","https://example.test"},{"Notifications:PartnerEmail:Enabled",emailEnabled ? "true" : "false"}}).Build()};
            f.Company.IsPartnerEnabled=true;f.Owner.PartnerCompanyId=f.Company.Id;var branch=Branch.Create(f.Company.Id,"Apartment");
            var request=new PartnerCleaningRequest{CompanyId=f.Company.Id,BranchId=branch.Id,Status="Accepted",Notes="private access code",FromDate=new(2026,11,1),ToDate=new(2026,11,1)};
            f.Notification.CompanyId=f.Company.Id;f.Notification.BranchId=branch.Id;f.Notification.RequestId=request.Id;f.Notification.NextAttemptUtc=f.Now.UtcDateTime;
            using var seed=store.LightweightSession(f.TenantId);seed.Store(f.Company);seed.Store(f.Owner);seed.Store(branch);seed.Store(request);seed.Store(f.Notification);await seed.SaveChangesAsync();return f;
        }
        public TenantSessionFactory Session(){var tenant=new Mock<ITenantContext>();tenant.SetupGet(t=>t.TenantId).Returns(TenantId);return new(Store,tenant.Object);}
        public PartnerNotificationDispatcher Dispatcher(TenantSessionFactory session)
        {
            var tenant=new Mock<ITenantContext>();tenant.SetupGet(t=>t.TenantId).Returns(TenantId);var registry=new Mock<ITenantRegistry>();registry.Setup(r=>r.GetBySlugAsync(TenantId,It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration{Slug=TenantId,Status=TenantRegistration.StatusActive});
            var clock=new Mock<TimeProvider>();clock.Setup(c=>c.GetUtcNow()).Returns(()=>Now);
            return new(new PartnerStore(session),new ShiftWorkReservation(Config,session),Transport.Object,registry.Object,tenant.Object,clock.Object,Config,NullLogger<PartnerNotificationDispatcher>.Instance,new PartnerNotificationReservation(Config,session));
        }
        public void Dispose()=>Provider.Dispose();
    }
}
