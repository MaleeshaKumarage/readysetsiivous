using System.Data;
using CleaningSuite.Application.Agreements;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Moq;

namespace CleaningSuite.Tests.Agreements;

public class AgreementRepositoryTests
{
    [Fact]
    public async Task SaveAsync_StoresInTenantSession()
    {
        var session = new Mock<IDocumentSession>();
        var store = new Mock<IDocumentStore>();
        store.Setup(s => s.DirtyTrackedSession("readysetsiivous", It.IsAny<IsolationLevel>())).Returns(session.Object);

        var repo = new AgreementRepository(store.Object);
        var doc = new Agreement { Slug = "readysetsiivous", Title = "T" };

        await repo.SaveAsync("readysetsiivous", doc);

        session.Verify(s => s.Store(doc), Times.Once);
        session.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
