using System.Net;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CleaningSuite.Tests;

public class HealthzTests
{
    [Fact]
    public async Task Healthz_returns_200_without_database()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<StoreOptions>(opts =>
                {
                    opts.AutoCreateSchemaObjects = AutoCreate.None;
                });
            });
        });

        var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
