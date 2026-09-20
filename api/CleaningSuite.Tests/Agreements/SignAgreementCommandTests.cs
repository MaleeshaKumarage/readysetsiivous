using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Agreements;
using Moq;

namespace CleaningSuite.Tests.Agreements;

public class SignAgreementCommandTests
{
    [Fact]
    public async Task Sign_LastSigner_CompletesAndGeneratesPdf()
    {
        var token = "tok1";
        var a = new Agreement
        {
            Slug = "readysetsiivous", Title = "T", Status = Agreement.StatusPartiallySigned,
            OriginalPdfPath = "/u/original.pdf",
        };
        a.Signers.Add(new Signer { Name = "A", Email = "a@x.fi", Token = token, Status = Signer.StatusPending });

        var repo = new Mock<IAgreementRepository>();
        repo.Setup(r => r.FindBySignerTokenAsync("readysetsiivous", token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(a);
        var store = new Mock<IAgreementFileStore>();
        store.Setup(s => s.SaveSignatureAsync("readysetsiivous", a.Id, It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/u/sig.png");
        store.Setup(s => s.SaveSignedAsync("readysetsiivous", a.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/u/signed.pdf");
        var gen = new Mock<IAgreementDocumentGenerator>();
        gen.Setup(g => g.GenerateSignedPdfAsync("/u/original.pdf", It.IsAny<IReadOnlyList<Signer>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<Signer>, string, CancellationToken>(
                (_, _, outputPath, _) => File.WriteAllBytes(outputPath, [1, 2, 3]))
            .Returns(Task.CompletedTask);

        var handler = new SignAgreementHandler(MockTenant(), repo.Object, store.Object, gen.Object);
        var result = await handler.Handle(new SignAgreementCommand(token, "Alice", [1, 2, 3]), CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(1, result.SignedCount);
        Assert.Equal(1, result.TotalSigners);
        Assert.Equal(Agreement.StatusCompleted, a.Status);
        Assert.Equal("/u/signed.pdf", a.SignedPdfPath);
        gen.Verify(g => g.GenerateSignedPdfAsync("/u/original.pdf", It.IsAny<IReadOnlyList<Signer>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.SaveSignedAsync("readysetsiivous", a.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sign_UnknownToken_ThrowsNotFound()
    {
        var repo = new Mock<IAgreementRepository>();
        repo.Setup(r => r.FindBySignerTokenAsync("readysetsiivous", "nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agreement?)null);
        var store = new Mock<IAgreementFileStore>();
        var gen = new Mock<IAgreementDocumentGenerator>();
        var handler = new SignAgreementHandler(MockTenant(), repo.Object, store.Object, gen.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SignAgreementCommand("nope", "X", [1]), CancellationToken.None));
    }

    private static CleaningSuite.Application.Tenants.ITenantContext MockTenant()
    {
        var c = new Mock<CleaningSuite.Application.Tenants.ITenantContext>();
        c.SetupGet(x => x.TenantId).Returns("readysetsiivous");
        return c.Object;
    }
}
