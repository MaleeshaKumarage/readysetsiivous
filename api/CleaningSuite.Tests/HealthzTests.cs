using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CleaningSuite.Tests;

public class HealthzTests
{
    [Fact]
    public async Task Healthz_returns_200_without_database()
    {
        var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
