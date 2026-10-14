using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Domain.Companies;
using Moq;

namespace CleaningSuite.Tests.Companies;

public class CompanyParentTests
{
    [Fact]
    public async Task CreateBranch_MissingCompany_DoesNotWriteBranch()
    {
        var companies = new Mock<ICompanyRepository>();
        var branches = new Mock<IBranchRepository>();
        var handler = new CompanyHandlers.BranchCommands.CreateBranchCommandHandler(branches.Object, companies.Object);
        var command = new CreateBranchCommand(Guid.NewGuid(), "Branch");

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));

        companies.Verify(c => c.GetAsync(command.CompanyId, CancellationToken.None), Times.Once);
        branches.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateBranch_WaitsForCompanyLookupBeforeWriting()
    {
        var companyId = Guid.NewGuid();
        var lookup = new TaskCompletionSource<Company?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(c => c.GetAsync(companyId, It.IsAny<CancellationToken>())).Returns(lookup.Task);
        var branches = new Mock<IBranchRepository>();
        var handler = new CompanyHandlers.BranchCommands.CreateBranchCommandHandler(branches.Object, companies.Object);

        var creation = handler.Handle(new CreateBranchCommand(companyId, "Branch"), CancellationToken.None);
        Assert.False(creation.IsCompleted);
        branches.VerifyNoOtherCalls();

        lookup.SetResult(new Company { Id = companyId, IsActive = false });
        var result = await creation;

        Assert.Equal(companyId, result.CompanyId);
        Assert.Equal("Branch", result.Name);
        branches.Verify(b => b.SaveAsync(It.Is<Branch>(branch => branch.CompanyId == companyId), CancellationToken.None), Times.Once);
    }

    [Fact]
    public void CreateBranch_EmptyCompanyId_IsInvalid()
    {
        var result = new CompanyValidators.CreateBranchCommandValidator()
            .Validate(new CreateBranchCommand(Guid.Empty, "Branch"));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateBranchCommand.CompanyId));
    }
}
