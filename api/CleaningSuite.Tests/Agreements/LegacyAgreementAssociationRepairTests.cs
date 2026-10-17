using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Companies;
using Moq;

namespace CleaningSuite.Tests.Agreements;

public sealed class LegacyAgreementAssociationRepairTests
{
    [Fact]
    public async Task Preview_OnlyReturnsUnsignedDraftOrCancelledWithMissingCompany()
    {
        var eligible = Agreement(Agreement.StatusDraft);
        var cancelled = Agreement(Agreement.StatusCancelled);
        var signedEvidence = Agreement(Agreement.StatusDraft);
        signedEvidence.Signers[0].SignatureImagePath = "/private/evidence.png";
        var validParent = Agreement(Agreement.StatusDraft);
        validParent.CompanyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var agreements = new Mock<IAgreementRepository>();
        agreements.Setup(x => x.ListMissingCompanyAssociationsAsync("tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync([eligible, cancelled, signedEvidence, validParent]);
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(x => x.ListAsync(null, 0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Company>)new[] { Company(validParent.CompanyId) }, 1));
        var handler = new PreviewLegacyAgreementRepairsHandler(Tenant(), agreements.Object, companies.Object);

        var result = await handler.Handle(new PreviewLegacyAgreementRepairsQuery(), CancellationToken.None);

        Assert.Equal(new[] { eligible.Id, cancelled.Id }, result.Agreements.Select(x => x.AgreementId));
        Assert.All(result.Agreements, x => Assert.Equal(Guid.Empty, x.MissingCompanyId));
        agreements.Verify(x => x.SaveAssociationRepairAsync(It.IsAny<string>(), It.IsAny<Agreement>(), It.IsAny<AgreementAssociationRepairAudit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_UsesExplicitMappingAndAtomicallySavesAudit()
    {
        var agreement = Agreement(Agreement.StatusDraft);
        var companyId = Guid.NewGuid();
        var agreements = new Mock<IAgreementRepository>();
        agreements.Setup(x => x.GetAssociationRepairAuditAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgreementAssociationRepairAudit?)null);
        agreements.Setup(x => x.GetAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(x => x.GetAsync(Guid.Empty, It.IsAny<CancellationToken>())).ReturnsAsync((Company?)null);
        companies.Setup(x => x.GetAsync(companyId, It.IsAny<CancellationToken>())).ReturnsAsync(Company(companyId));
        agreements.Setup(x => x.SaveAssociationRepairAsync("tenant-a", agreement,
                It.Is<AgreementAssociationRepairAudit>(a => a.Id == agreement.Id && a.PreviousCompanyId == Guid.Empty && a.CompanyId == companyId && a.ActorId == "user-7"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = new RepairLegacyAgreementAssociationHandler(Tenant(), agreements.Object, companies.Object);

        var result = await handler.Handle(new(agreement.Id, companyId, agreement.Version, "user-7"), CancellationToken.None);

        Assert.Equal(agreement.Id, result.AgreementId);
        Assert.Equal(companyId, agreement.CompanyId);
        agreements.Verify(x => x.SaveAssociationRepairAsync("tenant-a", agreement, It.IsAny<AgreementAssociationRepairAudit>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_RejectsStaleVersionAndNeverSaves()
    {
        var agreement = Agreement(Agreement.StatusDraft);
        var agreements = new Mock<IAgreementRepository>();
        agreements.Setup(x => x.GetAssociationRepairAuditAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgreementAssociationRepairAudit?)null);
        agreements.Setup(x => x.GetAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        var handler = new RepairLegacyAgreementAssociationHandler(Tenant(), agreements.Object, Mock.Of<ICompanyRepository>());

        await Assert.ThrowsAsync<AgreementConflictException>(() => handler.Handle(
            new(agreement.Id, Guid.NewGuid(), Guid.NewGuid(), "user-7"), CancellationToken.None));

        agreements.Verify(x => x.SaveAssociationRepairAsync(It.IsAny<string>(), It.IsAny<Agreement>(), It.IsAny<AgreementAssociationRepairAudit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_RejectsSignedEvidenceEvenWhenStatusLooksDraft()
    {
        var agreement = Agreement(Agreement.StatusDraft);
        agreement.Signers[0].TypedName = "Alice";
        var agreements = new Mock<IAgreementRepository>();
        agreements.Setup(x => x.GetAssociationRepairAuditAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgreementAssociationRepairAudit?)null);
        agreements.Setup(x => x.GetAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(x => x.GetAsync(Guid.Empty, It.IsAny<CancellationToken>())).ReturnsAsync((Company?)null);
        var handler = new RepairLegacyAgreementAssociationHandler(Tenant(), agreements.Object, companies.Object);

        await Assert.ThrowsAsync<AgreementConflictException>(() => handler.Handle(
            new(agreement.Id, Guid.NewGuid(), agreement.Version, "user-7"), CancellationToken.None));

        agreements.Verify(x => x.SaveAssociationRepairAsync(It.IsAny<string>(), It.IsAny<Agreement>(), It.IsAny<AgreementAssociationRepairAudit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_ReplaysSameTargetAndRejectsDifferentTarget()
    {
        var agreement = Agreement(Agreement.StatusDraft);
        var targetId = Guid.NewGuid();
        agreement.CompanyId = targetId;
        var agreements = new Mock<IAgreementRepository>();
        agreements.Setup(x => x.GetAssociationRepairAuditAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgreementAssociationRepairAudit { Id = agreement.Id, AgreementId = agreement.Id, PreviousCompanyId = Guid.Empty, CompanyId = targetId });
        agreements.Setup(x => x.GetAsync("tenant-a", agreement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agreement);
        var handler = new RepairLegacyAgreementAssociationHandler(Tenant(), agreements.Object, Mock.Of<ICompanyRepository>());

        var replay = await handler.Handle(new(agreement.Id, targetId, Guid.NewGuid(), "user-7"), CancellationToken.None);
        Assert.Equal(Guid.Empty, replay.MissingCompanyId);
        await Assert.ThrowsAsync<AgreementConflictException>(() => handler.Handle(
            new(agreement.Id, Guid.NewGuid(), Guid.NewGuid(), "user-7"), CancellationToken.None));
        agreements.Verify(x => x.SaveAssociationRepairAsync(It.IsAny<string>(), It.IsAny<Agreement>(), It.IsAny<AgreementAssociationRepairAudit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Agreement Agreement(string status)
    {
        var agreement = new Agreement { Slug = "tenant-a", Title = "Service contract", Status = status, CompanyId = Guid.Empty, Version = Guid.NewGuid() };
        agreement.Signers.Add(new Signer { Name = "A", Email = "a@example.test", Token = "secret-token" });
        return agreement;
    }

    private static Company Company(Guid id) => new() { Id = id, Name = "Company", BusinessId = "123", IsActive = true };

    private static ITenantContext Tenant()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(x => x.TenantId).Returns("tenant-a");
        return tenant.Object;
    }
}
