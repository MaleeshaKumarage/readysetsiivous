using System;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Agreements;

public class SignedOrCancelledLinkTests
{
    private static Agreement AgreementWith(string status, string token)
    {
        var agreement = new Agreement { Id = Guid.NewGuid(), Status = status };
        agreement.Signers.Add(new Signer { Token = token, Name = "Anna", Email = "anna@example.test" });
        return agreement;
    }

    private static async Task<AgreementConflictException> Refused(Agreement agreement)
    {
        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns("synthetic");
        var repo = new Mock<IAgreementRepository>();
        repo.Setup(x => x.FindBySignerTokenAsync("synthetic", "token", It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        var handler = new SignAgreementHandler(context.Object, repo.Object, new Mock<IAgreementFileStore>().Object,
            new Mock<IAgreementDocumentGenerator>().Object, new Mock<IUploadContentValidator>().Object);
        return await Assert.ThrowsAsync<AgreementConflictException>(() =>
            handler.Handle(new SignAgreementCommand("token", "Anna", new byte[] { 1 }), default));
    }

    [Fact]
    public async Task Signing_a_cancelled_agreement_says_it_cannot_be_signed_and_what_to_do()
    {
        var ex = await Refused(AgreementWith(Agreement.StatusCancelled, "token"));
        Assert.Contains("cannot be signed", ex.Message);
        Assert.Contains("Ask the sender", ex.Message);
    }

    [Fact]
    public async Task Signing_again_says_the_agreement_is_already_signed_and_no_action_is_needed()
    {
        var agreement = AgreementWith(Agreement.StatusCompleted, "token");
        agreement.Signers[0].Status = Signer.StatusSigned;
        var ex = await Refused(agreement);
        Assert.Contains("already signed", ex.Message);
        Assert.Contains("No further action is needed", ex.Message);
    }
}
