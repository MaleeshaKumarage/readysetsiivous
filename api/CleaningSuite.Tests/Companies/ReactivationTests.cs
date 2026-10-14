using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using Moq;

namespace CleaningSuite.Tests.Companies;

public class ReactivationTests
{
    [Fact]
    public async Task Company_ReactivationPreservesContactMetadataAndIdentity()
    {
        var company = Company.Create("123", "Client", "Person", "a@example.com", "555", "Private notes");
        company.Deactivate();
        var created = company.CreatedUtc;
        var repo = new Mock<ICompanyRepository>();
        repo.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        await new CompanyHandlers.ReactivateCompanyCommandHandler(repo.Object).Handle(new(company.Id), default);
        Assert.True(company.IsActive);
        Assert.Equal(("123", "Client", "Person", "a@example.com", "555", "Private notes", created),
            (company.BusinessId, company.Name, company.ContactName, company.ContactEmail, company.ContactPhone, company.Notes, company.CreatedUtc));
        repo.Verify(r => r.SaveAsync(company, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Branch_ReactivationPreservesAddressAndDoesNotActivateParent()
    {
        var company = Company.Create("123", "Client"); company.Deactivate();
        var branch = Branch.Create(company.Id, "Office", "Street", "12345", "City", "Country", "555"); branch.Deactivate();
        var branches = new Mock<IBranchRepository>(); var companies = new Mock<ICompanyRepository>();
        branches.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        companies.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        await new CompanyHandlers.BranchCommands.ReactivateBranchCommandHandler(branches.Object, companies.Object).Handle(new(branch.Id), default);
        Assert.True(branch.IsActive); Assert.False(company.IsActive);
        Assert.Equal((company.Id, "Office", "Street", "12345", "City", "Country", "555"),
            (branch.CompanyId, branch.Name, branch.Street, branch.PostalCode, branch.City, branch.Country, branch.ContactPhone));
        companies.Verify(r => r.SaveAsync(It.IsAny<Company>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Branch_MissingTenantParentDoesNotReactivateOrSave()
    {
        var branch = Branch.Create(Guid.NewGuid(), "Office"); branch.Deactivate();
        var branches = new Mock<IBranchRepository>();
        branches.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        await Assert.ThrowsAsync<NotFoundException>(() => new CompanyHandlers.BranchCommands.ReactivateBranchCommandHandler(branches.Object, Mock.Of<ICompanyRepository>()).Handle(new(branch.Id), default));
        Assert.False(branch.IsActive);
        branches.Verify(r => r.SaveAsync(It.IsAny<Branch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Template_ChangesOnlyActivityAndPreservesScopeAndChecklist(bool active)
    {
        var company = Company.Create("123", "Client"); company.Deactivate();
        var branch = Branch.Create(company.Id, "Office"); branch.Deactivate();
        var template = QualityCycleTemplate.Create("Checklist", ["Floor", "Windows"], "Notes", company.Id, branch.Id);
        template.IsActive = !active;
        var created = template.CreatedUtc;
        var repo = new Mock<IQualityCycleRepository>(); var companies = new Mock<ICompanyRepository>(); var branches = new Mock<IBranchRepository>();
        repo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        companies.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        branches.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        await new QualityCycleHandlers.SetQualityCycleTemplateActiveCommandHandler(repo.Object, companies.Object, branches.Object).Handle(new(template.Id, active), default);
        Assert.Equal(active, template.IsActive); Assert.False(company.IsActive); Assert.False(branch.IsActive);
        Assert.Equal((company.Id, branch.Id, "Checklist", "Notes", created), (template.CompanyId, template.BranchId, template.Title, template.Description, template.CreatedUtc));
        Assert.Equal(new[] { "Floor", "Windows" }, template.Items);
        repo.Verify(r => r.SaveTemplateAsync(template, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.DeleteTemplateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Template_MismatchedParentDoesNotReactivateOrSave()
    {
        var company = Company.Create("123", "Client");
        var branch = Branch.Create(Guid.NewGuid(), "Other tenant branch");
        var template = QualityCycleTemplate.Create("Checklist", ["Floor"], companyId: company.Id, branchId: branch.Id); template.IsActive = false;
        var repo = new Mock<IQualityCycleRepository>(); var companies = new Mock<ICompanyRepository>(); var branches = new Mock<IBranchRepository>();
        repo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        companies.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        branches.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        await Assert.ThrowsAsync<NotFoundException>(() => new QualityCycleHandlers.SetQualityCycleTemplateActiveCommandHandler(repo.Object, companies.Object, branches.Object).Handle(new(template.Id, true), default));
        Assert.False(template.IsActive);
        repo.Verify(r => r.SaveTemplateAsync(It.IsAny<QualityCycleTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
