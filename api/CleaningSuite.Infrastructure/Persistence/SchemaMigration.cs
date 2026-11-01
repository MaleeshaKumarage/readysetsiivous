using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>Explicit release operation; never invoked by the runtime hosted service.</summary>
public static class SchemaMigration
{
    public static async Task RunAsync(string connectionString)
    {
        // The login is deliberately NOINHERIT. SET ROLE is selected for every
        // connection, including Marten's connections, so future objects have the
        // stable owner to which default privileges are attached.
        var connection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Options = "-c role=cleaning_owner",
            Pooling = false
        };
        await using var db = new NpgsqlConnection(connection.ConnectionString);
        await db.OpenAsync();
        // Serialize release migrations, retaining this dedicated connection
        // until all Marten connections have finished. Disposal releases it.
        await using var reserve = new NpgsqlCommand("SET lock_timeout = '30s'; SELECT pg_advisory_lock(643917251984)", db);
        await reserve.ExecuteNonQueryAsync();
        await using var check = new NpgsqlCommand("""
            SELECT current_user = 'cleaning_owner'
              AND NOT (SELECT rolsuper OR rolcreatedb OR rolcreaterole FROM pg_roles WHERE rolname = session_user)
              AND session_user = 'cleaning_migrator'
              AND NOT EXISTS (
                SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relname LIKE 'mt_%'
                  AND c.relowner <> 'cleaning_owner'::regrole)
              AND NOT EXISTS (
                SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'public' AND p.proname LIKE 'mt_%'
                  AND p.proowner <> 'cleaning_owner'::regrole)
            """, db);
        if (await check.ExecuteScalarAsync() is not true)
            throw new InvalidOperationException("Migration identity or existing Marten ownership is unsafe. Review the ownership inventory and rollout; no automatic ownership transfer is performed.");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(connection.ConnectionString, AutoCreate.CreateOrUpdate);
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        _ = store.Storage.ToDatabaseScript();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
