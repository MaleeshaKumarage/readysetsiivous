using CleaningSuite.Domain.Employees;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CleaningSuite.Tests.Employees;

// Runs the shipped migration block (between its markers) against a real Marten schema.
public class AutoAcceptMigrationDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Migration_switches_only_employees_who_never_chose_and_is_idempotent()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var schema = "auto_accept_" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: schema);
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "auto-accept-" + Guid.NewGuid().ToString("N");

        var legacyFalse = new Employee { Role = Employee.RoleEmployee, AutoAcceptWork = false, Email = "legacy@example.test" };
        var chosenFalse = new Employee { Role = Employee.RoleEmployee, AutoAcceptWork = false, AutoAcceptWorkChosenUtc = DateTime.UtcNow, Email = "chose-off@example.test" };
        var chosenTrue = new Employee { Role = Employee.RoleManager, AutoAcceptWork = true, AutoAcceptWorkChosenUtc = DateTime.UtcNow, Email = "chose-on@example.test" };
        var legacyAdmin = new Employee { Role = Employee.RoleAdmin, AutoAcceptWork = false, Email = "admin@example.test" };
        var partner = new Employee { Role = Employee.RolePartner, AutoAcceptWork = false, Email = "partner@example.test" };
        var defaulted = new Employee { Role = Employee.RoleEmployee, Email = "default@example.test" };
        using (var seed = store.LightweightSession(tenant))
        {
            seed.Store(legacyFalse, chosenFalse, chosenTrue, legacyAdmin, partner, defaulted);
            await seed.SaveChangesAsync();
        }

        var migration = ShippedMigrationBlock();
        await using var data = NpgsqlDataSource.Create(connection);
        await using (var connectionScope = await data.OpenConnectionAsync())
        {
            await Exec(connectionScope, $"SET search_path TO {schema}");
            await Exec(connectionScope, migration);
        }
        Assert.Equal(new Dictionary<string, bool>
        {
            [legacyFalse.Id.ToString()] = true,
            [chosenFalse.Id.ToString()] = false,
            [chosenTrue.Id.ToString()] = true,
            [legacyAdmin.Id.ToString()] = true,
            [partner.Id.ToString()] = false,
            [defaulted.Id.ToString()] = true,
        }, await AutoAcceptValues(data, schema));

        // A second run changes nothing, so a retried release cannot flip a later deliberate opt-out.
        await using (var connectionScope = await data.OpenConnectionAsync())
        {
            await Exec(connectionScope, $"SET search_path TO {schema}");
            await Exec(connectionScope, migration);
        }
        Assert.False((await AutoAcceptValues(data, schema))[chosenFalse.Id.ToString()]);
    }

    private static string ShippedMigrationBlock()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        while (directory is not null && path is null)
        {
            var candidate = Path.Combine(directory.FullName, "api", "deploy", "employee-auto-accept-migration.sql");
            if (File.Exists(candidate)) path = candidate;
            directory = directory.Parent;
        }
        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        var start = text.IndexOf("-- BEGIN MIGRATION", StringComparison.Ordinal);
        var end = text.IndexOf("-- END MIGRATION", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return text[(start + "-- BEGIN MIGRATION".Length)..end];
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<string, bool>> AutoAcceptValues(NpgsqlDataSource data, string schema)
    {
        var values = new Dictionary<string, bool>();
        await using var connection = await data.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT id, (data ->> 'AutoAcceptWork')::boolean FROM {schema}.mt_doc_employee", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values[reader.GetGuid(0).ToString()] = reader.GetBoolean(1);
        return values;
    }
}
