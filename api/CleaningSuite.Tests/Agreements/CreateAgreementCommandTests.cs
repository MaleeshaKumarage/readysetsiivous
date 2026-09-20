using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Domain.Agreements;
using Moq;

namespace CleaningSuite.Tests.Agreements;

public class CreateAgreementCommandTests
{
    [Fact]
    public async Task Create_StoresPdf_AssignsTokens()
    {
        var repo = new Mock<IAgreementRepository>();
        var store = new Mock<IAgreementFileStore>();
        store.Setup(s => s.SaveOriginalAsync("readysetsiivous", It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/uploads/agreements/original.pdf");

        var handler = new CreateAgreementHandler(MockTenant(), repo.Object, store.Object);
        var cmd = new CreateAgreementCommand("Service agreement", Stream.Null, "a.pdf",
            new[] { new SignerInput("Alice", "alice@x.fi"), new SignerInput("Bob", "bob@x.fi") });

        var dto = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal("Draft", dto.Status);
        Assert.Equal(2, dto.Signers.Count);
        Assert.All(dto.Signers, s => Assert.False(string.IsNullOrEmpty(s.Token)));
        repo.Verify(r => r.SaveAsync("readysetsiivous",
            It.Is<Agreement>(a => a.Signers.Count == 2 && a.Signers.All(s => s.Token.Length > 0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static CleaningSuite.Application.Tenants.ITenantContext MockTenant()
    {
        var c = new Mock<CleaningSuite.Application.Tenants.ITenantContext>();
        c.SetupGet(x => x.TenantId).Returns("readysetsiivous");
        return c.Object;
    }
}
