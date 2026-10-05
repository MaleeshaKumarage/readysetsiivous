using CleaningSuite.Application.Companies;
using CleaningSuite.Domain.Companies;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Companies;

public class BranchQueryTests
{
    [Fact]
    public async Task ListBranchesQuery_ReturnsBranchesForCompany()
    {
        var companyId = Guid.NewGuid();
        var branchRepoMock = new Mock<IBranchRepository>();
        var branches = new List<Branch>
        {
            Branch.Create(companyId, "Helsinki Central", "Street 1", "00100", "Helsinki", "Finland", "+358123456"),
            Branch.Create(companyId, "Espoo Branch", "Street 2", "02100", "Espoo", "Finland", "+358654321")
        };

        branchRepoMock.Setup(r => r.ListByCompanyAsync(companyId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branches);

        var handler = new CompanyHandlers.ListBranchesQueryHandler(branchRepoMock.Object);
        var result = await handler.Handle(new ListBranchesQuery(companyId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Helsinki Central", result[0].Name);
        Assert.Equal("Espoo Branch", result[1].Name);
    }
}
