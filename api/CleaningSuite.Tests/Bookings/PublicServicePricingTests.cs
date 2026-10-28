using CleaningSuite.Application.Services;
using CleaningSuite.Application.Services.Queries;
using CleaningSuite.Domain.Services;
using Moq;

namespace CleaningSuite.Tests.Bookings;

public class PublicServicePricingTests
{
    [Theory]
    [InlineData(50, 25.5, 62.75)]
    [InlineData(0.10, 25, 0.12)]
    [InlineData(50, 0, 50)]
    public async Task Public_price_is_the_same_per_service_total_as_booking(decimal net, decimal vat, decimal expected)
    {
        var repository = new Mock<IServiceRepository>();
        repository.Setup(x => x.ListAsync(false, default)).ReturnsAsync(new[] {
            new Service { PriceNet = net, VatRatePercent = vat, DurationMinutes = 120, Currency = "EUR" }
        });
        var result = Assert.Single(await new GetPublicServicesHandler(repository.Object).Handle(new("en"), default));
        Assert.Equal(expected, result.PriceGross);
        Assert.Equal(120, result.DurationMinutes);
        Assert.Equal("EUR", result.Currency);
    }
}
