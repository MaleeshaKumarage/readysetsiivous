using CleaningSuite.Application.Notifications;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Infrastructure.Notifications;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Notifications;

public class PushDeliveryDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Retention_scrubs_old_credentials_even_when_more_than_a_batch_of_recently_disabled_devices_exists()
    {
        using var f = await Fixture.Create();
        var expired = new PushDevice { Enabled = false, Endpoint = "https://fcm.googleapis.com/old-secret", P256dh = "old-key", Auth = "old-auth", UpdatedUtc = f.Now.UtcDateTime.AddDays(-31) };
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            for (var n = 0; n < 101; n++) db.Store(new PushDevice { Enabled = false, Endpoint = "https://fcm.googleapis.com/recent-" + n, P256dh = "recent-key", Auth = "recent-auth", UpdatedUtc = f.Now.UtcDateTime.AddDays(-1) });
            db.Store(expired); await db.SaveChangesAsync();
        }
        using var session = f.Session(); await f.Dispatcher(session).ProcessAsync();
        using var query = f.Store.QuerySession(f.Tenant);
        var cleaned = (await query.LoadAsync<PushDevice>(expired.Id))!;
        Assert.Empty(cleaned.Endpoint); Assert.Empty(cleaned.P256dh); Assert.Empty(cleaned.Auth);
        Assert.Equal(101, await query.Query<PushDevice>().CountAsync(d => !d.Enabled && d.Endpoint != ""));
    }
    [QualityCycleDatabaseFact]
    public async Task Key_rotation_releases_all_old_device_slots_for_a_new_browser_endpoint()
    {
        using var f = await Fixture.Create();
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            // The fixture already owns one old-key device; fill the remaining slots.
            for (var n = 1; n < 10; n++)
                db.Store(new PushDevice { EmployeeId = f.Employee.Id, Subject = "subject", VapidKeyId = "key", UpdatedUtc = f.Now.UtcDateTime.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        using var session = f.Session();
        var settings = Mock.Of<IPushSettings>(x => x.Available == true && x.KeyId == "rotated");
        var clock = new Mock<TimeProvider>(); clock.Setup(x => x.GetUtcNow()).Returns(f.Now);
        var subscriptions = new PushSubscriptions(session, new ShiftWorkReservation(f.Config, session), settings, f.Transport.Object, clock.Object);
        var keys = WebPush.VapidHelper.GenerateVapidKeys();
        var auth = Convert.ToBase64String(new byte[16]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var input = new PushSubscriptionInput("https://fcm.googleapis.com/fcm/send/new-after-rotation", new(keys.PublicKey, auth));

        var enrolled = await subscriptions.EnrollAsync(new(f.Employee.Id, "subject"), input);

        Assert.True(enrolled.Enabled);
        using var query = f.Store.QuerySession(f.Tenant);
        var devices = await query.Query<PushDevice>().ToListAsync();
        Assert.Equal(11, devices.Count);
        var current = Assert.Single(devices.Where(x => x.Enabled));
        Assert.Equal(enrolled.Id, current.Id); Assert.Equal("rotated", current.VapidKeyId);
        Assert.All(devices.Where(x => x.Id != current.Id), x => { Assert.False(x.Enabled); Assert.Equal(f.Now.UtcDateTime, x.UpdatedUtc); });
    }

    [QualityCycleDatabaseFact]
    public async Task Subscription_ownership_limits_rate_limit_rotation_and_tenant_isolation()
    {
        using var f = await Fixture.Create(); using var session = f.Session();
        var keys = WebPush.VapidHelper.GenerateVapidKeys();
        var auth = Convert.ToBase64String(new byte[16]).TrimEnd('=').Replace('+','-').Replace('/','_');
        var settings = new Mock<IPushSettings>(); settings.SetupGet(s => s.Available).Returns(true); settings.SetupGet(s => s.KeyId).Returns("key");
        var clock = new Mock<TimeProvider>(); clock.Setup(c => c.GetUtcNow()).Returns(() => f.Now);
        var subscriptions = new PushSubscriptions(session, new ShiftWorkReservation(f.Config, session), settings.Object, f.Transport.Object, clock.Object);
        var actor = new PushActor(f.Employee.Id, "subject");
        PushSubscriptionInput Input(int n) => new("https://fcm.googleapis.com/fcm/send/device-" + n, new(keys.PublicKey, auth));
        var first = await subscriptions.EnrollAsync(actor, Input(1));
        Assert.Equal(first.Id, (await subscriptions.EnrollAsync(actor, Input(1))).Id);
        // One device was seeded by the delivery fixture, leaving nine new slots.
        for (var n = 2; n <= 9; n++) await subscriptions.EnrollAsync(actor, Input(n));
        Assert.Equal("device_limit", (await Assert.ThrowsAsync<PushSubscriptionException>(() => subscriptions.EnrollAsync(actor, Input(10)))).Code);
        f.Transport.Setup(t => t.SendAsync(It.IsAny<PushDevice>(), It.IsAny<PushMessage>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PushSendResult(true));
        Assert.True((await subscriptions.TestAsync(actor, first.Id)).Accepted);
        Assert.Equal("rate_limited", (await Assert.ThrowsAsync<PushSubscriptionException>(() => subscriptions.TestAsync(actor, first.Id))).Code);
        await subscriptions.DisableAsync(actor, first.Id);
        Assert.Equal("device_not_found", (await Assert.ThrowsAsync<PushSubscriptionException>(() => subscriptions.TestAsync(actor, first.Id))).Code);
        Assert.Equal(first.Id, (await subscriptions.EnrollAsync(actor, Input(1))).Id);
        settings.SetupGet(s => s.KeyId).Returns("rotated");
        Assert.False((await subscriptions.GetAsync(actor)).Devices.Single(d => d.Id == first.Id).Enabled);
        await subscriptions.EnrollAsync(actor, Input(1));
        Assert.True((await subscriptions.GetAsync(actor)).Devices.Single(d => d.Id == first.Id).Enabled);
        var other = new Employee { KeycloakUserId = "other-subject" };
        using (var db = f.Store.LightweightSession(f.Tenant)) { db.Store(other); await db.SaveChangesAsync(); }
        Assert.Equal("ownership_conflict", (await Assert.ThrowsAsync<PushSubscriptionException>(() => subscriptions.EnrollAsync(new(other.Id, other.KeycloakUserId), Input(1)))).Code);
        Assert.Equal("device_not_found", (await Assert.ThrowsAsync<PushSubscriptionException>(() => subscriptions.DisableAsync(new(other.Id, other.KeycloakUserId), first.Id))).Code);
        var foreignContext = new Mock<ITenantContext>(); foreignContext.SetupGet(c => c.TenantId).Returns(f.Tenant + "-other");
        using var foreign = new TenantSessionFactory(f.Store, foreignContext.Object);
        using (var db = f.Store.LightweightSession(f.Tenant + "-other")) { db.Store(f.Employee); await db.SaveChangesAsync(); }
        var foreignService = new PushSubscriptions(foreign, new ShiftWorkReservation(f.Config, foreign), settings.Object, f.Transport.Object, clock.Object);
        Assert.Empty((await foreignService.GetAsync(actor)).Devices);
        Assert.Equal("device_not_found", (await Assert.ThrowsAsync<PushSubscriptionException>(() => foreignService.DisableAsync(actor, first.Id))).Code);
        Assert.Equal("device_not_found", (await Assert.ThrowsAsync<PushSubscriptionException>(() => foreignService.TestAsync(actor, first.Id))).Code);
    }
    [QualityCycleDatabaseFact]
    public async Task Two_replicas_reserve_delivery_without_reserving_scheduling_and_commit_outbox_atomically()
    {
        using var f = await Fixture.Create(); using var first = f.Session(); using var second = f.Session(); using var scheduling = f.Session();
        using (var discarded = f.Store.LightweightSession(f.Tenant))
            await new PushOutbox().StageScheduleChangeAsync(discarded, Guid.NewGuid(), [f.Employee.Id], f.Now, default);
        using (var q = f.Store.QuerySession(f.Tenant)) Assert.Empty(await q.Query<PushIntent>().ToListAsync());
        using (var committed = f.Store.LightweightSession(f.Tenant))
        {
            await new PushOutbox().StageScheduleChangeAsync(committed, Guid.NewGuid(), [f.Employee.Id], f.Now, default);
            await committed.SaveChangesAsync();
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.Setup(t => t.SendAsync(It.IsAny<PushDevice>(), It.IsAny<PushMessage>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns<PushDevice, PushMessage, DateTime, CancellationToken>(async (_, _, _, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return new(true); });
        var send = f.Dispatcher(first).ProcessAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var work = await new ShiftWorkReservation(f.Config, scheduling).AcquireAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await f.Dispatcher(second).ProcessAsync().WaitAsync(TimeSpan.FromSeconds(3));
            f.Transport.Verify(t => t.SendAsync(It.IsAny<PushDevice>(), It.IsAny<PushMessage>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { release.TrySetResult(); await send; }
        using var query = f.Store.QuerySession(f.Tenant);
        Assert.Equal("Accepted", (await query.Query<PushDelivery>().SingleAsync()).State);
    }

    [QualityCycleDatabaseFact]
    public async Task Rebound_subject_is_suppressed_and_new_devices_do_not_receive_old_events()
    {
        using var f = await Fixture.Create();
        using (var db = f.Store.LightweightSession(f.Tenant))
        {
            await new PushOutbox().StageScheduleChangeAsync(db, Guid.NewGuid(), [f.Employee.Id], f.Now, default);
            f.Employee.KeycloakUserId = "replacement"; db.Store(f.Employee);
            db.Store(new PushDevice { EmployeeId = f.Employee.Id, Subject = "replacement", OptedInUtc = f.Now.UtcDateTime.AddSeconds(1), CreatedUtc = f.Now.UtcDateTime.AddSeconds(1), VapidKeyId = "key" });
            await db.SaveChangesAsync();
        }
        using var session = f.Session(); await f.Dispatcher(session).ProcessAsync();
        f.Transport.Verify(t => t.SendAsync(It.IsAny<PushDevice>(), It.IsAny<PushMessage>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        using var q = f.Store.QuerySession(f.Tenant); Assert.Single(await q.Query<PushDelivery>().ToListAsync());
    }

    [QualityCycleDatabaseFact]
    public async Task Retry_after_is_respected_and_expired_provider_subscription_is_disabled()
    {
        using var f = await Fixture.Create(); using var session = f.Session();
        using (var db = f.Store.LightweightSession(f.Tenant)) { await new PushOutbox().StageScheduleChangeAsync(db, Guid.NewGuid(), [f.Employee.Id], f.Now); await db.SaveChangesAsync(); }
        f.Transport.SetupSequence(t => t.SendAsync(It.IsAny<PushDevice>(), It.IsAny<PushMessage>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult(false, Retryable: true, RetryAfter: TimeSpan.FromMinutes(5)))
            .ReturnsAsync(new PushSendResult(false, Expired: true));
        await f.Dispatcher(session).ProcessAsync();
        f.Now = f.Now.AddMinutes(4); await f.Dispatcher(session).ProcessAsync();
        f.Transport.Verify(t => t.SendAsync(It.IsAny<PushDevice>(), It.IsAny<PushMessage>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Now = f.Now.AddMinutes(2); await f.Dispatcher(session).ProcessAsync();
        using var q = f.Store.QuerySession(f.Tenant);
        Assert.False((await q.Query<PushDevice>().SingleAsync()).Enabled);
        var delivery = await q.Query<PushDelivery>().SingleAsync(); Assert.Equal("Failed", delivery.State); Assert.Equal(2, delivery.Attempts);
    }

    private sealed class Fixture : IDisposable
    {
        public required ServiceProvider Provider; public required IDocumentStore Store; public required IConfiguration Config;
        public string Tenant = "push-test-" + Guid.NewGuid().ToString("N");
        public DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        public Employee Employee = new() { KeycloakUserId = "subject" };
        public Mock<IPushTransport> Transport = new();
        public static async Task<Fixture> Create()
        {
            var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
            var services = new ServiceCollection(); services.AddLogging(); services.AddCleaningMarten(connection, databaseSchemaName: "push_core_" + Guid.NewGuid().ToString("N"));
            var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>(); await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            var f = new Fixture { Provider = provider, Store = store, Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build() };
            using var db = store.LightweightSession(f.Tenant); db.Store(f.Employee);
            db.Store(new PushDevice { EmployeeId = f.Employee.Id, Subject = "subject", VapidKeyId = "key", OptedInUtc = f.Now.UtcDateTime.AddMinutes(-1), CreatedUtc = f.Now.UtcDateTime.AddMinutes(-1) });
            await db.SaveChangesAsync(); return f;
        }
        public TenantSessionFactory Session() { var context = new Mock<ITenantContext>(); context.SetupGet(t => t.TenantId).Returns(Tenant); return new(Store, context.Object); }
        public PushDeliveryService Dispatcher(TenantSessionFactory session)
        {
            var settings = new Mock<IPushSettings>(); settings.SetupGet(s => s.Available).Returns(true); settings.SetupGet(s => s.KeyId).Returns("key");
            var eligible = new Mock<IPushDeliveryEligibility>(); eligible.Setup(e => e.IsEligibleAsync(It.IsAny<PushIntent>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var clock = new Mock<TimeProvider>(); clock.Setup(c => c.GetUtcNow()).Returns(() => Now);
            return new(session, new PushDeliveryReservation(Config, session), settings.Object, Transport.Object, eligible.Object, clock.Object);
        }
        public void Dispose() => Provider.Dispose();
    }
}
