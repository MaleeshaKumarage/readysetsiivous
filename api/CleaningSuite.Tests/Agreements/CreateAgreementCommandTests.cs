using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Companies;
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

        var cmd = new CreateAgreementCommand("Service agreement", Guid.NewGuid(), Stream.Null, "a.pdf",
            new[] { new SignerInput("Alice", "alice@x.fi"), new SignerInput("Bob", "bob@x.fi") });
        var companies = new Mock<ICompanyRepository>();
        var uploads = new Mock<IUploadContentValidator>();
        companies.Setup(c => c.GetAsync(cmd.CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = cmd.CompanyId });
        var handler = new CreateAgreementHandler(MockTenant(), repo.Object, store.Object, companies.Object, uploads.Object);

        var dto = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal("Draft", dto.Status);
        Assert.Equal(2, dto.Signers.Count);
        Assert.All(dto.Signers, s => Assert.False(string.IsNullOrEmpty(s.Token)));
        store.Verify(s => s.SaveOriginalAsync("readysetsiivous", It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveAsync("readysetsiivous",
            It.Is<Agreement>(a => a.Signers.Count == 2 && a.Signers.All(s => s.Token.Length > 0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_MissingCompany_DoesNotStorePdfOrAgreement()
    {
        var companies = new Mock<ICompanyRepository>();
        var repo = new Mock<IAgreementRepository>();
        var store = new Mock<IAgreementFileStore>();
        var uploads = new Mock<IUploadContentValidator>();
        var handler = new CreateAgreementHandler(MockTenant(), repo.Object, store.Object, companies.Object, uploads.Object);
        var command = new CreateAgreementCommand("Agreement", Guid.NewGuid(), Stream.Null, "a.pdf",
            new[] { new SignerInput("Alice", "alice@x.fi") });

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));

        companies.Verify(c => c.GetAsync(command.CompanyId, CancellationToken.None), Times.Once);
        store.VerifyNoOtherCalls();
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_WaitsForCompanyLookupBeforeStoringPdf()
    {
        var companyId = Guid.NewGuid();
        var lookup = new TaskCompletionSource<Company?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(c => c.GetAsync(companyId, It.IsAny<CancellationToken>())).Returns(lookup.Task);
        var repo = new Mock<IAgreementRepository>();
        var store = new Mock<IAgreementFileStore>();
        var uploads = new Mock<IUploadContentValidator>();
        store.Setup(s => s.SaveOriginalAsync("readysetsiivous", It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("original.pdf");
        var handler = new CreateAgreementHandler(MockTenant(), repo.Object, store.Object, companies.Object, uploads.Object);
        var command = new CreateAgreementCommand("Agreement", companyId, Stream.Null, "a.pdf",
            new[] { new SignerInput("Alice", "alice@x.fi") });

        var creation = handler.Handle(command, CancellationToken.None);
        Assert.False(creation.IsCompleted);
        store.VerifyNoOtherCalls();
        repo.VerifyNoOtherCalls();

        lookup.SetResult(new Company { Id = companyId, IsActive = false });
        var result = await creation;
        Assert.Equal("Agreement", result.Title);
        store.Verify(s => s.SaveOriginalAsync("readysetsiivous", It.IsAny<Guid>(), It.IsAny<Stream>(), CancellationToken.None), Times.Once);
        repo.Verify(r => r.SaveAsync("readysetsiivous", It.Is<Agreement>(a => a.CompanyId == companyId), CancellationToken.None), Times.Once);
    }

    private static CleaningSuite.Application.Tenants.ITenantContext MockTenant()
    {
        var c = new Mock<CleaningSuite.Application.Tenants.ITenantContext>();
        c.SetupGet(x => x.TenantId).Returns("readysetsiivous");
        return c.Object;
    }
}
