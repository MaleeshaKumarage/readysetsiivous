using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using Moq;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleTemplateParentTests
{
    [QualityCycleDatabaseFact]
    public async Task ForeignCompanyAndBranch_AreRejectedByProductionTenantRepositories()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "qc_parent_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var company = Company.Create("FOREIGN", "Foreign company");
        var branch = Branch.Create(company.Id, "Foreign branch");
        using (var foreign = store.LightweightSession("tenant-b"))
        {
            foreign.Store(company);
            foreign.Store(branch);
            await foreign.SaveChangesAsync();
        }
        var context = new Mock<ITenantContext>();
        context.SetupGet(c => c.TenantId).Returns("tenant-a");
        using var tenant = new TenantSessionFactory(store, context.Object);
        var companies = new CompanyRepository(tenant);
        var branches = new BranchRepository(tenant);
        var templates = new QualityCycleRepository(tenant, store);
        var existing = QualityCycleTemplate.Create("Existing", new() { "Item" });
        await templates.SaveTemplateAsync(existing);
        var create = new QualityCycleHandlers.CreateQualityCycleTemplateCommandHandler(templates, companies, branches);
        var update = new QualityCycleHandlers.UpdateQualityCycleTemplateCommandHandler(templates, companies, branches);
        foreach (var scope in new (Guid? Company, Guid? Branch)[] { (company.Id, null), (null, branch.Id), (company.Id, branch.Id) })
        {
            await Assert.ThrowsAsync<NotFoundException>(() => create.Handle(new("Rejected", new() { "Item" }, CompanyId: scope.Company, BranchId: scope.Branch), default));
            await Assert.ThrowsAsync<NotFoundException>(() => update.Handle(new(existing.Id, "Rejected", new() { "Changed" }, CompanyId: scope.Company, BranchId: scope.Branch), default));
        }
        using var reader = store.QuerySession("tenant-a");
        var saved = await reader.LoadAsync<QualityCycleTemplate>(existing.Id);
        Assert.Equal("Existing", saved!.Title);
        Assert.Null(saved.CompanyId);
        Assert.Null(saved.BranchId);
        Assert.Single(await reader.Query<QualityCycleTemplate>().ToListAsync());
        using var foreignReader = store.QuerySession("tenant-b");
        Assert.Equal(company.BusinessId, (await foreignReader.LoadAsync<Company>(company.Id))!.BusinessId);
        Assert.Equal(company.Id, (await foreignReader.LoadAsync<Branch>(branch.Id))!.CompanyId);
    }

    private readonly Mock<IQualityCycleRepository> _templates = new();
    private readonly Mock<ICompanyRepository> _companies = new();
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    public QualityCycleTemplateParentTests()
    {
        _companies.Setup(c => c.GetAsync(_companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = _companyId, IsActive = false });
        _branches.Setup(b => b.GetAsync(_branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = _branchId, CompanyId = _companyId, IsActive = false });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ValidScopes_ArePreservedOnCreateAndUpdate(bool company, bool branch)
    {
        Guid? companyId = company ? _companyId : null;
        Guid? branchId = branch ? _branchId : null;
        var create = new QualityCycleHandlers.CreateQualityCycleTemplateCommandHandler(_templates.Object, _companies.Object, _branches.Object);
        var dto = await create.Handle(new("Template", new() { "Item" }, CompanyId: companyId, BranchId: branchId), default);
        Assert.Equal(companyId, dto.CompanyId);
        Assert.Equal(branchId, dto.BranchId);
        var existing = QualityCycleTemplate.Create("Old", new() { "Old item" });
        _templates.Setup(t => t.GetTemplateAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var update = new QualityCycleHandlers.UpdateQualityCycleTemplateCommandHandler(_templates.Object, _companies.Object, _branches.Object);
        await update.Handle(new(existing.Id, "New", new() { "Item" }, CompanyId: companyId, BranchId: branchId), default);
        Assert.Equal(companyId, existing.CompanyId);
        Assert.Equal(branchId, existing.BranchId);
        _templates.Verify(t => t.SaveTemplateAsync(It.IsAny<QualityCycleTemplate>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        if (!company && !branch) { _companies.VerifyNoOtherCalls(); _branches.VerifyNoOtherCalls(); }
        if (!company && branch) _companies.Verify(c => c.GetAsync(_companyId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("branch")]
    [InlineData("mismatch")]
    [InlineData("orphan")]
    public async Task InvalidScope_PreventsCreateAndLeavesUpdateUnchanged(string failure)
    {
        Guid? companyId = failure == "orphan" ? null : _companyId;
        Guid? branchId = failure == "company" ? null : _branchId;
        if (failure == "company") companyId = Guid.NewGuid();
        if (failure == "branch") branchId = Guid.NewGuid();
        if (failure is "mismatch" or "orphan")
            _branches.Setup(b => b.GetAsync(_branchId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Branch { Id = _branchId, CompanyId = Guid.NewGuid() });
        var create = new QualityCycleHandlers.CreateQualityCycleTemplateCommandHandler(_templates.Object, _companies.Object, _branches.Object);
        await Assert.ThrowsAsync<NotFoundException>(() => create.Handle(new("New", new() { "Item" }, CompanyId: companyId, BranchId: branchId), default));
        var existing = QualityCycleTemplate.Create("Old", new() { "Old item" }, "Old description", _companyId, _branchId);
        var updatedUtc = existing.UpdatedUtc;
        _templates.Setup(t => t.GetTemplateAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var update = new QualityCycleHandlers.UpdateQualityCycleTemplateCommandHandler(_templates.Object, _companies.Object, _branches.Object);
        await Assert.ThrowsAsync<NotFoundException>(() => update.Handle(new(existing.Id, "New", new() { "New item" }, "New description", false, companyId, branchId), default));
        Assert.Equal("Old", existing.Title);
        Assert.Equal("Old description", existing.Description);
        Assert.Equal(new[] { "Old item" }, existing.Items);
        Assert.True(existing.IsActive);
        Assert.Equal(_companyId, existing.CompanyId);
        Assert.Equal(_branchId, existing.BranchId);
        Assert.Equal(updatedUtc, existing.UpdatedUtc);
        _templates.Verify(t => t.SaveTemplateAsync(It.IsAny<QualityCycleTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyOptionalIds_AreInvalid(bool company)
    {
        Guid? companyId = company ? Guid.Empty : null;
        Guid? branchId = company ? null : Guid.Empty;
        var create = new QualityCycleValidators.CreateQualityCycleTemplateCommandValidator()
            .Validate(new CreateQualityCycleTemplateCommand("Template", new() { "Item" }, CompanyId: companyId, BranchId: branchId));
        var update = new QualityCycleValidators.UpdateQualityCycleTemplateCommandValidator()
            .Validate(new UpdateQualityCycleTemplateCommand(Guid.NewGuid(), "Template", new() { "Item" }, CompanyId: companyId, BranchId: branchId));
        Assert.Contains(create.Errors, e => e.PropertyName == (company ? "CompanyId" : "BranchId"));
        Assert.Contains(update.Errors, e => e.PropertyName == (company ? "CompanyId" : "BranchId"));
    }
}
