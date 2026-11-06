using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Companies;

public class DeactivateCompanyWithShiftsTests
{
    [Fact]
    public async Task A_company_with_active_shifts_is_not_deactivated_and_the_admin_is_told_what_to_do()
    {
        var company = Company.Create("1234567-8", "Customer");
        var repository = new Mock<ICompanyRepository>();
        repository.Setup(x => x.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = true }]);
        var ex = await Assert.ThrowsAsync<ShiftConflictException>(() =>
            new CompanyHandlers.DeactivateCompanyCommandHandler(repository.Object, null, shifts.Object).Handle(new(company.Id), default));
        Assert.Contains("Deactivate or move them first", ex.Message);
        Assert.True(company.IsActive);
        repository.Verify(x => x.SaveAsync(It.IsAny<Company>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_company_whose_shifts_are_all_inactive_can_be_deactivated()
    {
        var company = Company.Create("1234567-8", "Customer");
        var repository = new Mock<ICompanyRepository>();
        repository.Setup(x => x.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = false }]);
        await new CompanyHandlers.DeactivateCompanyCommandHandler(repository.Object, null, shifts.Object).Handle(new(company.Id), default);
        repository.Verify(x => x.SaveAsync(company, It.IsAny<CancellationToken>()), Times.Once);
    }
}
