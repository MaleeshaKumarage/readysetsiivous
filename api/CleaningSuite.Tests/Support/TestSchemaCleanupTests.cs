using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleaningSuite.Tests.Support;

[Collection("Database integration")]
public class TestSchemaCleanupTests
{
    [QualityCycleDatabaseFact]
    public async Task Sync_and_async_provider_disposal_drop_only_their_fixture_schema()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var schemas = new[] { "cleanup_" + Guid.NewGuid().ToString("N"), "cleanup_" + Guid.NewGuid().ToString("N") };
        async Task<ServiceProvider> Create(string schema)
        {
            var services = new ServiceCollection(); services.AddLogging(); services.AddTestMarten(connection, databaseSchemaName: schema);
            var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDocumentStore>().Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            return provider;
        }
        using var first = await Create(schemas[0]);
        await using var second = await Create(schemas[1]);
        async Task<bool> Exists(string schema)
        {
            await using var db = new NpgsqlConnection(connection); await db.OpenAsync();
            await using var cmd = new NpgsqlCommand("select exists(select 1 from pg_namespace where nspname = @schema)", db);
            cmd.Parameters.AddWithValue("schema", schema); return (bool)(await cmd.ExecuteScalarAsync())!;
        }
        Assert.True(await Exists(schemas[0])); Assert.True(await Exists(schemas[1]));
        first.Dispose(); Assert.False(await Exists(schemas[0])); Assert.True(await Exists(schemas[1]));
        await second.DisposeAsync(); Assert.False(await Exists(schemas[1]));
    }
}
