using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.Tenants.Commands;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Tenants;
using Moq;

namespace CleaningSuite.Tests.Tenants;

public class TenantProfileIdentityTests
{
    [Fact]
    public async Task First_save_of_a_missing_profile_uses_the_id_the_reader_looks_up()
    {
        const string tenant = "readysetsiivous";
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.TenantId).Returns(tenant);
        var saved = new TenantProfile();
        var profiles = new Mock<ITenantProfileRepository>();
        profiles.Setup(x => x.GetAsync(tenant, It.IsAny<CancellationToken>())).ReturnsAsync((TenantProfile?)null);
        profiles.Setup(x => x.SaveAsync(tenant, It.IsAny<TenantProfile>(), It.IsAny<CancellationToken>()))
            .Callback<string, TenantProfile, CancellationToken>((_, p, _) => saved = p)
            .Returns(Task.CompletedTask);

        var handler = new UpdateTenantProfileHandler(context.Object, profiles.Object);
        await handler.Handle(new UpdateTenantProfileCommand(
            "ReadySetSiivous", "", new AddressDto("", "", "", "FI"), "", "", "", "",
            "Europe/Helsinki", "fi", 25.5m, 14, true, 2), default);

        Assert.Equal(TenantDocumentIds.TenantProfile(tenant), saved.Id);
        Assert.Equal("Europe/Helsinki", saved.TimeZoneId);
    }
}
