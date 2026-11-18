using CleaningSuite.Application.Companies;
using CleaningSuite.Domain.Companies;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Companies;

public class BranchNameRuleTests
{
    private static Branch Named(Guid companyId, string name) => Branch.Create(companyId, name, null, null, null, null, null);

    [Theory]
    [InlineData("Lake  Apartment", "lake apartment", true)]
    [InlineData("  Lake Apartment ", "Lake Apartment", true)]
    [InlineData("Lake Apartment", "Lake Apartment 2", false)]
    public void Names_match_ignoring_case_and_extra_spaces(string a, string b, bool same)
    {
        Assert.Equal(same, !BranchNameRule.Changed(a, b));
    }

    [Fact]
    public void Duplicate_name_in_the_same_company_is_rejected_but_not_the_branch_being_renamed()
    {
        var companyId = Guid.NewGuid();
        var lake = Named(companyId, "Lake Apartment");
        var conflict = Assert.Throws<BranchNameConflictException>(() => BranchNameRule.EnsureUnique([lake], " lake   APARTMENT ", null));
        Assert.Contains("already exists in this company", conflict.Message);
        BranchNameRule.EnsureUnique([lake], "Lake Apartment", lake.Id); // renaming a branch to its own name is fine
    }

    [Fact]
    public async Task Creating_a_duplicate_property_is_refused_and_nothing_is_saved()
    {
        var companyId = Guid.NewGuid();
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(x => x.GetAsync(companyId, It.IsAny<CancellationToken>())).ReturnsAsync(new Company { Id = companyId });
        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.ListByCompanyAsync(companyId, true, It.IsAny<CancellationToken>())).ReturnsAsync([Named(companyId, "Lake Apartment")]);
        var handler = new CompanyHandlers.BranchCommands.CreateBranchCommandHandler(branches.Object, companies.Object);

        await Assert.ThrowsAsync<BranchNameConflictException>(() => handler.Handle(new CreateBranchCommand(companyId, "lake apartment"), CancellationToken.None));
        branches.Verify(x => x.SaveAsync(It.IsAny<Branch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_same_name_in_a_different_company_is_allowed()
    {
        var companyId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(x => x.GetAsync(companyId, It.IsAny<CancellationToken>())).ReturnsAsync(new Company { Id = companyId });
        var branches = new Mock<IBranchRepository>();
        // The repository scopes the list to one company, so the other company's "Lake Apartment" is never returned here.
        branches.Setup(x => x.ListByCompanyAsync(companyId, true, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        branches.Setup(x => x.ListByCompanyAsync(other, true, It.IsAny<CancellationToken>())).ReturnsAsync([Named(other, "Lake Apartment")]);
        var handler = new CompanyHandlers.BranchCommands.CreateBranchCommandHandler(branches.Object, companies.Object);

        await handler.Handle(new CreateBranchCommand(companyId, "Lake Apartment"), CancellationToken.None);
        branches.Verify(x => x.ListByCompanyAsync(other, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        branches.Verify(x => x.SaveAsync(It.IsAny<Branch>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Renaming_to_an_existing_name_is_refused_but_an_unchanged_legacy_duplicate_can_still_be_edited()
    {
        var companyId = Guid.NewGuid();
        var lake = Named(companyId, "Lake Apartment");
        var legacyTwin = Named(companyId, "Lake Apartment");
        var pier = Named(companyId, "Pier House");
        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.GetAsync(lake.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lake);
        branches.Setup(x => x.GetAsync(pier.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pier);
        branches.Setup(x => x.ListByCompanyAsync(companyId, true, It.IsAny<CancellationToken>())).ReturnsAsync([lake, legacyTwin, pier]);
        var handler = new CompanyHandlers.BranchCommands.UpdateBranchCommandHandler(branches.Object);

        await Assert.ThrowsAsync<BranchNameConflictException>(() => handler.Handle(new UpdateBranchCommand(pier.Id, "Lake Apartment"), CancellationToken.None));
        // Same name, different spacing and case: nothing is renamed, so the legacy duplicate does not block the edit.
        await handler.Handle(new UpdateBranchCommand(lake.Id, "lake  apartment", City: "Helsinki"), CancellationToken.None);
        branches.Verify(x => x.SaveAsync(lake, It.IsAny<CancellationToken>()), Times.Once);
    }
}
