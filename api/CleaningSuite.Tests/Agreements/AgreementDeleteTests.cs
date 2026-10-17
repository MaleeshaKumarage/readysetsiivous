using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Agreements;

public class AgreementDeleteTests
{
    [Fact]
    public async Task Delete_Requires_Explicit_Confirmation_For_Signed_Evidence()
    {
        var repository = new Mock<IAgreementRepository>();
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(x => x.TenantId).Returns("tenant-a");
        var agreement = new Agreement { Id = Guid.NewGuid(), IsActive = false,
            Signers = [new Signer { Status = Signer.StatusSigned, SignedAtUtc = DateTime.UtcNow }] };
        repository.Setup(x => x.GetAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        var handler = new DeleteAgreementHandler(tenant.Object, repository.Object);

        var error = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() =>
            handler.Handle(new DeleteAgreementCommand(agreement.Id), default));

        Assert.Contains("signed evidence", error.Message.ToLowerInvariant());
        repository.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(Agreement.StatusPartiallySigned)]
    [InlineData(Agreement.StatusCompleted)]
    public async Task Delete_Removes_Inactive_Signed_Agreement_When_Explicitly_Confirmed(string status)
    {
        var repository = new Mock<IAgreementRepository>();
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(x => x.TenantId).Returns("tenant-a");
        var agreement = new Agreement { Id = Guid.NewGuid(), IsActive = false, Status = status,
            SignedPdfPath = "signed.pdf", Signers = [new Signer { Status = Signer.StatusSigned }] };
        repository.Setup(x => x.GetAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        await new DeleteAgreementHandler(tenant.Object, repository.Object).Handle(new(agreement.Id, ConfirmSigned: true), default);
        repository.Verify(x => x.DeleteAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_Still_Rejects_Active_Agreement_With_Confirmation()
    {
        var repository = new Mock<IAgreementRepository>();
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(x => x.TenantId).Returns("tenant-a");
        var agreement = new Agreement { Id = Guid.NewGuid(), IsActive = true, Status = Agreement.StatusCompleted };
        repository.Setup(x => x.GetAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() =>
            new DeleteAgreementHandler(tenant.Object, repository.Object).Handle(new(agreement.Id, ConfirmSigned: true), default));
        repository.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_Removes_Inactive_Unsigned_Agreement()
    {
        var repository = new Mock<IAgreementRepository>();
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(x => x.TenantId).Returns("tenant-a");
        var agreement = new Agreement { Id = Guid.NewGuid(), IsActive = false };
        repository.Setup(x => x.GetAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        var handler = new DeleteAgreementHandler(tenant.Object, repository.Object);

        await handler.Handle(new DeleteAgreementCommand(agreement.Id), default);

        repository.Verify(x => x.DeleteAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
