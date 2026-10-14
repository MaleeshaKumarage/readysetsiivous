using System.Text.Json;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Services;
using CleaningSuite.Application.Services.Commands;
using CleaningSuite.Domain.Services;
using Moq;
using CleaningSuite.Api.Controllers;
using CleaningSuite.Api.Auth;
using Microsoft.AspNetCore.Authorization;

namespace CleaningSuite.Tests.Services;

public class ServiceActivityTests
{
    [Theory]
    [InlineData(nameof(ServicesAdminController.Reactivate))]
    [InlineData(nameof(ServicesAdminController.Deactivate))]
    public void ActivityCommandsRequireAdminPolicy(string action)
    {
        var attributes = typeof(ServicesAdminController).GetMethod(action)!.GetCustomAttributes(typeof(AuthorizeAttribute), true);
        Assert.Contains(attributes.Cast<AuthorizeAttribute>(), a => a.Policy == RealmRoleAuthorization.PolicyPrefix + "admin");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StatusOnlyMutationPreservesMetadataAndIsIdempotent(bool active)
    {
        var service = new Service { IsActive = !active, Slug = "retained-service", Category = "special", Currency = "SEK",
            ImageUrl = "/uploads/retained.png", Icon = "Star", DurationMinutes = 135, PriceNet = 123.45m,
            VatRatePercent = 12, IsFeatured = true, SortOrder = 42 };
        service.Name.Values = new() { ["fi"] = "Name", ["sv"] = "Namn" };
        service.Description.Values = new() { ["fi"] = "Description" };
        service.AdditionalInfo.Values = new() { ["fi"] = "Special retained instructions" };
        var before = JsonSerializer.SerializeToNode(service)!.AsObject();
        var repository = new Mock<IServiceRepository>();
        repository.Setup(r => r.GetByIdAsync(service.Id, It.IsAny<CancellationToken>())).ReturnsAsync(service);
        var handler = new SetServiceActivityHandler(repository.Object);
        await handler.Handle(new(service.Id, active), default);
        await handler.Handle(new(service.Id, active), default);
        var after = JsonSerializer.SerializeToNode(service)!.AsObject();
        before.Remove("IsActive"); before.Remove("UpdatedUtc");
        after.Remove("IsActive"); after.Remove("UpdatedUtc");
        Assert.Equal(before.ToJsonString(), after.ToJsonString());
        Assert.Equal(active, service.IsActive);
        repository.Verify(r => r.SaveAsync(service, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MissingOrOtherTenantServiceDoesNotWrite()
    {
        var repository = new Mock<IServiceRepository>();
        await Assert.ThrowsAsync<NotFoundException>(() => new SetServiceActivityHandler(repository.Object).Handle(new(Guid.NewGuid(), true), default));
        repository.Verify(r => r.SaveAsync(It.IsAny<Service>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
