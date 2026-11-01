using CleaningSuite.Domain.Common;
using CleaningSuite.Infrastructure.Persistence;
using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleaningSuite.Tests;

public class DatabasePrivilegeFactAttribute : FactAttribute
{
    public DatabasePrivilegeFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DB_PRIVILEGE_RUNTIME_CONNECTION")))
            Skip = "Requires isolated PostgreSQL roles provisioned by database/setup.sh.";
    }
}

public class DatabasePrivilegeTests
{
    [DatabasePrivilegeFact]
    public async Task Exact_mappings_migrate_idempotently_and_runtime_can_work_without_DDL()
    {
        var migration = Environment.GetEnvironmentVariable("DB_PRIVILEGE_MIGRATION_CONNECTION")!;
        var runtime = Environment.GetEnvironmentVariable("DB_PRIVILEGE_RUNTIME_CONNECTION")!;
        await SchemaMigration.RunAsync(migration);
        await SchemaMigration.RunAsync(migration);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(runtime, AutoCreate.None);
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        var ownerConnection = new NpgsqlConnectionStringBuilder(migration) { Options = "-c role=cleaning_owner" };
        await using (var owner = new NpgsqlConnection(ownerConnection.ConnectionString))
        {
            await owner.OpenAsync();
            await using var create = new NpgsqlCommand("""
                CREATE SEQUENCE IF NOT EXISTS public.privilege_fixture_sequence;
                CREATE OR REPLACE FUNCTION public.privilege_fixture_function() RETURNS integer
                LANGUAGE sql SECURITY INVOKER AS 'SELECT 42';
                """, owner);
            await create.ExecuteNonQueryAsync();
        }
        var tenant = "privilege-" + Guid.NewGuid().ToString("N");
        var doc = new Counter { Name = "synthetic", Value = 1 };
        using (var writer = store.LightweightSession(tenant))
        {
            writer.Store(doc);
            await writer.SaveChangesAsync();
            doc.Value = 2;
            writer.Store(doc);
            await writer.SaveChangesAsync();
        }
        using (var own = store.QuerySession(tenant))
            Assert.Equal(2, (await own.LoadAsync<Counter>(doc.Id))!.Value);
        using (var foreign = store.QuerySession(tenant + "-other"))
            Assert.Null(await foreign.LoadAsync<Counter>(doc.Id));
        using (var writer = store.LightweightSession(tenant))
        {
            writer.Delete<Counter>(doc.Id);
            await writer.SaveChangesAsync();
        }
        await using var db = new NpgsqlConnection(runtime);
        await db.OpenAsync();
        await using (var future = new NpgsqlCommand("SELECT nextval('public.privilege_fixture_sequence'); SELECT public.privilege_fixture_function()", db))
            await future.ExecuteNonQueryAsync();
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_lock(481937); SELECT pg_advisory_unlock(481937)", db))
            await advisory.ExecuteNonQueryAsync();
        foreach (var forbidden in new[] {
            "CREATE TABLE public.runtime_must_not_create(id integer)",
            "CREATE SCHEMA runtime_must_not_create",
            "ALTER TABLE public.mt_doc_counter ADD COLUMN forbidden integer",
            "TRUNCATE public.mt_doc_counter",
            "CREATE ROLE runtime_must_not_create",
            "CREATE DATABASE runtime_must_not_create",
            "SET ROLE cleaning_owner",
            "SET ROLE cleaning_migrator" })
        {
            await using var cmd = new NpgsqlCommand(forbidden, db);
            var error = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        foreach (var database in new[] { "keycloak", "postgres", "template1", "security_tests" })
        {
            var cross = new NpgsqlConnectionStringBuilder(runtime) { Database = database, Pooling = false };
            await using var denied = new NpgsqlConnection(cross.ConnectionString);
            var error = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        await Assert.ThrowsAnyAsync<Exception>(() => SchemaMigration.RunAsync(runtime));
    }
}
