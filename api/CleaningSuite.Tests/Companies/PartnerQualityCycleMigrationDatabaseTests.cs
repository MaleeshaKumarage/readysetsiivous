using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CleaningSuite.Tests.Companies;

// Runs the shipped branch-template clearing block (between its markers) against a real Marten schema.
public class PartnerQualityCycleMigrationDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Migration_clears_branch_templates_from_partner_companies_only_and_is_safe_to_rerun()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var schema = "partner_qc_clear_" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: schema);
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "partner-qc-clear-" + Guid.NewGuid().ToString("N");

        var companyTemplate = QualityCycleTemplate.Create("Company checklist", ["Corridor"], companyId: Guid.NewGuid());
        var branchTemplate = QualityCycleTemplate.Create("Branch checklist", ["Gym"], branchId: Guid.NewGuid());
        var partnerBranch = Company.Create("1234567-8", "Partner with branch template");
        partnerBranch.IsPartnerEnabled = true; partnerBranch.IsQualityCycleEnabled = true; partnerBranch.QualityCycleTemplateId = branchTemplate.Id;
        var partnerCompanyWide = Company.Create("2345678-9", "Partner with company template");
        partnerCompanyWide.IsPartnerEnabled = true; partnerCompanyWide.IsQualityCycleEnabled = true; partnerCompanyWide.QualityCycleTemplateId = companyTemplate.Id;
        var otherBranch = Company.Create("3456789-0", "Non-partner with branch template");
        otherBranch.IsQualityCycleEnabled = true; otherBranch.QualityCycleTemplateId = branchTemplate.Id;
        using (var seed = store.LightweightSession(tenant))
        {
            seed.Store(companyTemplate, branchTemplate);
            seed.Store(partnerBranch, partnerCompanyWide, otherBranch);
            await seed.SaveChangesAsync();
        }

        var migration = ShippedMigrationBlock();
        await using var data = NpgsqlDataSource.Create(connection);
        await RunMigration(data, schema, migration);

        Assert.Equal((false, (string?)null), await QualityCycleState(data, schema, partnerBranch.Id));
        Assert.Equal((true, companyTemplate.Id.ToString()), await QualityCycleState(data, schema, partnerCompanyWide.Id));
        Assert.Equal((true, branchTemplate.Id.ToString()), await QualityCycleState(data, schema, otherBranch.Id));

        // A second run changes nothing, so a retried release is safe.
        await RunMigration(data, schema, migration);
        Assert.Equal((false, (string?)null), await QualityCycleState(data, schema, partnerBranch.Id));
        Assert.Equal((true, branchTemplate.Id.ToString()), await QualityCycleState(data, schema, otherBranch.Id));
    }

    private static string ShippedMigrationBlock()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        while (directory is not null && path is null)
        {
            var candidate = Path.Combine(directory.FullName, "api", "deploy", "partner-quality-cycle-branch-clear.sql");
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

    private static async Task RunMigration(NpgsqlDataSource data, string schema, string migration)
    {
        await using var connection = await data.OpenConnectionAsync();
        await using (var set = new NpgsqlCommand($"SET search_path TO {schema}", connection)) await set.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand(migration, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(bool Enabled, string? TemplateId)> QualityCycleState(NpgsqlDataSource data, string schema, Guid companyId)
    {
        await using var connection = await data.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT data ->> 'IsQualityCycleEnabled', data ->> 'QualityCycleTemplateId' FROM {schema}.mt_doc_company WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", companyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0) == "true", reader.IsDBNull(1) ? null : reader.GetString(1));
    }
}
