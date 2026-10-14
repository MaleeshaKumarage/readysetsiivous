using CleaningSuite.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Tests;

public class MartenStartupTests
{
    [Fact]
    public void Unreachable_database_prevents_healthy_api_startup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=startup_test;Username=test;Password=test;Timeout=1",
                })));
        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.True(ContainsDatabaseFailure(failure), failure.ToString());
    }

    private static bool ContainsDatabaseFailure(Exception error) =>
        error is Npgsql.NpgsqlException
        || error.InnerException is not null && ContainsDatabaseFailure(error.InnerException)
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsDatabaseFailure);
}
