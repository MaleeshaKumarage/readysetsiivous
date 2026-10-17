using System.Text.Json;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleReportAcknowledgmentDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Acknowledgment_is_atomic_first_write_only_and_tenant_scoped_and_delivery_cannot_erase_it()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "report_receipt_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var report = new QualityCycleMonthlyReport(2026, 10, "Europe/Helsinki", now, []);
        var share = new QualityCycleReportShare { ReportJson = JsonSerializer.Serialize(report), ExpiresUtc = now.AddDays(30), RecipientEmail = "reader@example.test" };
        var tenant = "receipt-" + Guid.NewGuid().ToString("N");
        using (var writer = store.LightweightSession(tenant)) { writer.Store(share); await writer.SaveChangesAsync(); }
        QualityCycleRepository Repo(string partition, out TenantSessionFactory session) {
            var context = new Mock<ITenantContext>(); context.SetupGet(c => c.TenantId).Returns(partition);
            session = new TenantSessionFactory(store, context.Object); return new(session, store);
        }
        var repo = Repo(tenant, out var session); using var owned = session;
        var foreign = Repo(tenant + "-other", out var otherSession); using var other = otherSession;
        Assert.Null(await foreign.GetReportShareByTokenAsync(share.Token));
        await foreign.AcknowledgeReportShareAsync(share.Token, now);
        Assert.Null((await repo.GetReportShareByTokenAsync(share.Token))!.AcknowledgedUtc);
        await repo.AcknowledgeReportShareAsync(share.Token, now);
        await repo.AcknowledgeReportShareAsync(share.Token, now.AddHours(1));
        await repo.UpdateReportShareDeliveryAsync(share.Id, now.AddMinutes(1), null);
        var saved = (await repo.GetReportShareByTokenAsync(share.Token))!;
        Assert.Equal(now, saved.AcknowledgedUtc); Assert.Equal(now.AddMinutes(1), saved.EmailSentUtc);
        Assert.Equal(share.ReportJson, saved.ReportJson); Assert.Equal(share.ExpiresUtc, saved.ExpiresUtc);
        var clock = new Mock<TimeProvider>(); clock.Setup(c => c.GetUtcNow()).Returns(new DateTimeOffset(now.AddHours(2)));
        var handler = new PublicReportHandler(repo, clock.Object);
        var acknowledged = await handler.Handle(new AcknowledgeQualityCycleReportCommand(share.Token), default);
        Assert.Equal(now, acknowledged.AcknowledgedUtc);
        // Simulate an older retry failing after another request already sent successfully.
        await repo.UpdateReportShareDeliveryAsync(share.Id, null, "Later failure");
        // Simulate revocation after a stale share was loaded into the request session.
        await repo.GetReportShareAsync(share.Id);
        await repo.RevokeReportShareAsync(share.Id);
        var revokedShare = (await repo.GetReportShareByTokenAsync(share.Token))!;
        Assert.True(revokedShare.IsRevoked); Assert.Equal(now, revokedShare.AcknowledgedUtc);
        Assert.Equal(now.AddMinutes(1), revokedShare.EmailSentUtc); Assert.Null(revokedShare.EmailError);

        var racing = new QualityCycleReportShare { ReportJson = share.ReportJson, ExpiresUtc = now.AddDays(1) };
        using (var writer = store.LightweightSession(tenant)) { writer.Store(racing); await writer.SaveChangesAsync(); }
        await Task.WhenAll(repo.AcknowledgeReportShareAsync(racing.Token, now), repo.AcknowledgeReportShareAsync(racing.Token, now.AddSeconds(1)));
        var firstReceipt = (await repo.GetReportShareByTokenAsync(racing.Token))!.AcknowledgedUtc;
        Assert.True(firstReceipt == now || firstReceipt == now.AddSeconds(1));
        await repo.AcknowledgeReportShareAsync(racing.Token, now.AddHours(1));
        Assert.Equal(firstReceipt, (await repo.GetReportShareByTokenAsync(racing.Token))!.AcknowledgedUtc);

        // Expired/revoked shares cannot acquire acknowledgment, even via repository writes.
        foreach (var revoked in new[] { true, false }) {
            var blocked = new QualityCycleReportShare { ReportJson = share.ReportJson, IsRevoked = revoked, ExpiresUtc = revoked ? now.AddDays(1) : now };
            using (var writer = store.LightweightSession(tenant)) { writer.Store(blocked); await writer.SaveChangesAsync(); }
            await repo.AcknowledgeReportShareAsync(blocked.Token, now);
            Assert.Null((await repo.GetReportShareByTokenAsync(blocked.Token))!.AcknowledgedUtc);
        }
    }
}
