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
        var uploads = new Mock<IUploadContentValidator>();
        gen.Setup(g => g.GenerateSignedPdfAsync("/u/original.pdf", It.IsAny<IReadOnlyList<Signer>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<Signer>, string, CancellationToken>(
                (_, _, outputPath, _) => File.WriteAllBytes(outputPath, [1, 2, 3]))
            .Returns(Task.CompletedTask);

        var handler = new SignAgreementHandler(MockTenant(), repo.Object, store.Object, gen.Object, uploads.Object);
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
        var uploads = new Mock<IUploadContentValidator>();
        var handler = new SignAgreementHandler(MockTenant(), repo.Object, store.Object, gen.Object, uploads.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SignAgreementCommand("nope", "X", [1]), CancellationToken.None));
    }

    [Fact]
    public async Task Sign_SameTokenConflict_CleansOnlyLosingAttemptArtifacts()
    {
        const string token = "same-token";
        var initial = new Agreement { Slug = "readysetsiivous", Status = Agreement.StatusPartiallySigned, OriginalPdfPath = "/u/original.pdf" };
        initial.Signers.Add(new Signer { Name = "Alice", Email = "a@example.test", Token = token });
        var winner = new Agreement { Id = initial.Id, Slug = initial.Slug, Status = Agreement.StatusCompleted, OriginalPdfPath = initial.OriginalPdfPath, SignedPdfPath = "/u/winner.pdf" };
        winner.Signers.Add(new Signer { Id = initial.Signers[0].Id, Name = "Alice", Email = "a@example.test", Token = token,
            Status = Signer.StatusSigned, TypedName = "Alice", SignedAtUtc = DateTime.UtcNow, SignatureImagePath = "/u/winner.png" });

        var repo = new Mock<IAgreementRepository>();
        repo.Setup(r => r.FindBySignerTokenAsync("readysetsiivous", token, It.IsAny<CancellationToken>())).ReturnsAsync(initial);
        repo.Setup(r => r.ReloadBySignerTokenAsync("readysetsiivous", token, It.IsAny<CancellationToken>())).ReturnsAsync(winner);
        repo.Setup(r => r.SaveAsync("readysetsiivous", initial, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new JasperFx.ConcurrencyException("race"));
        var store = new Mock<IAgreementFileStore>();
        store.Setup(s => s.SaveSignatureAsync("readysetsiivous", initial.Id, initial.Signers[0].Id, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/u/loser.png");
        store.Setup(s => s.SaveSignedAsync("readysetsiivous", initial.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/u/loser.pdf");
        var generator = new Mock<IAgreementDocumentGenerator>();
        generator.Setup(g => g.GenerateSignedPdfAsync(initial.OriginalPdfPath, It.IsAny<IReadOnlyList<Signer>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<Signer>, string, CancellationToken>((_, _, path, _) => File.WriteAllBytes(path, [1, 2, 3]))
            .Returns(Task.CompletedTask);
        var handler = new SignAgreementHandler(MockTenant(), repo.Object, store.Object, generator.Object, new Mock<IUploadContentValidator>().Object);

        await Assert.ThrowsAsync<AgreementConflictException>(() => handler.Handle(new(token, "Alice", [1, 2, 3]), CancellationToken.None));

        Assert.Equal("/u/winner.png", winner.Signers[0].SignatureImagePath);
        Assert.Equal("/u/winner.pdf", winner.SignedPdfPath);
        store.Verify(s => s.DeleteArtifactAsync("readysetsiivous", initial.Id, "/u/loser.png", CancellationToken.None), Times.Once);
        store.Verify(s => s.DeleteArtifactAsync("readysetsiivous", initial.Id, "/u/loser.pdf", CancellationToken.None), Times.Once);
        store.Verify(s => s.DeleteArtifactAsync("readysetsiivous", initial.Id, "/u/winner.png", It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.DeleteArtifactAsync("readysetsiivous", initial.Id, "/u/winner.pdf", It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CleaningSuite.Application.Tenants.ITenantContext MockTenant()
    {
        var c = new Mock<CleaningSuite.Application.Tenants.ITenantContext>();
        c.SetupGet(x => x.TenantId).Returns("readysetsiivous");
        return c.Object;
    }
}
