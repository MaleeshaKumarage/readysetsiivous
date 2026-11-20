using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Leads;

public class LeadBusinessIdWarningTests
{
    private static LeadService Service(Mock<ILeadRepository> repository) =>
        new(repository.Object, Mock.Of<ILeadFileStore>(), NoopUploadContentValidator.Instance, TimeProvider.System);

    [Fact]
    public async Task A_new_lead_with_a_business_id_already_in_use_is_saved_with_a_warning()
    {
        var repository = new Mock<ILeadRepository>();
        repository.Setup(x => x.BusinessIdMatchesAsync("1234567-8", Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["customer \"Metehe Oy\""]);

        var detail = await Service(repository).CreateAsync(new LeadFields { Name = "Methehe Oy", BusinessId = "1234567-8" }, default);

        repository.Verify(x => x.SaveAsync(It.IsAny<Lead>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("1234567-8 is already used by customer \"Metehe Oy\"", detail.BusinessIdWarning);
        Assert.Contains("The lead was saved", detail.BusinessIdWarning);
    }

    [Fact]
    public async Task A_new_lead_with_an_unused_business_id_has_no_warning()
    {
        var repository = new Mock<ILeadRepository>();
        repository.Setup(x => x.BusinessIdMatchesAsync("7654321-0", Guid.Empty, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var detail = await Service(repository).CreateAsync(new LeadFields { Name = "Fresh Oy", BusinessId = "7654321-0" }, default);

        Assert.Null(detail.BusinessIdWarning);
        repository.Verify(x => x.SaveAsync(It.IsAny<Lead>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Editing_a_lead_checks_other_records_but_not_the_lead_itself()
    {
        var repository = new Mock<ILeadRepository>();
        var lead = new Lead { Id = Guid.NewGuid(), Version = Guid.NewGuid(), Fields = new LeadFields { Name = "Pier House", BusinessId = "1111111-1" }, IsActive = true };
        repository.Setup(x => x.GetAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        repository.Setup(x => x.LockAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IAsyncDisposable>());
        repository.Setup(x => x.BusinessIdMatchesAsync("1111111-1", lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var detail = await Service(repository).UpdateAsync(lead.Id, lead.Version, new LeadFields { Name = "Pier House", BusinessId = "1111111-1" }, default);

        Assert.Null(detail.BusinessIdWarning);
        repository.Verify(x => x.BusinessIdMatchesAsync("1111111-1", lead.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
