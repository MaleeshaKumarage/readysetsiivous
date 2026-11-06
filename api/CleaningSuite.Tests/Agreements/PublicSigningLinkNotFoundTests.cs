using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Agreements;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Agreements;

public class PublicSigningLinkNotFoundTests
{
    [Fact]
    public async Task Signing_with_an_unknown_link_says_to_ask_for_a_new_one_without_exposing_an_id()
    {
        var context = new Mock<CleaningSuite.Application.Tenants.ITenantContext>();
        context.SetupGet(x => x.TenantId).Returns("synthetic");
        var repo = new Mock<IAgreementRepository>();
        repo.Setup(x => x.FindBySignerTokenAsync("synthetic", "unknown-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CleaningSuite.Domain.Agreements.Agreement?)null);
        var uploads = new Mock<IUploadContentValidator>();
        var handler = new SignAgreementHandler(context.Object, repo.Object, new Mock<IAgreementFileStore>().Object,
            new Mock<IAgreementDocumentGenerator>().Object, uploads.Object);
        var ex = await Assert.ThrowsAsync<PublicSigningLinkNotFoundException>(() =>
            handler.Handle(new SignAgreementCommand("unknown-token", "Tester", new byte[] { 1, 2, 3 }), default));
        Assert.Contains("Ask the sender for a new signing link", ex.Message);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", ex.Message);
    }
}
