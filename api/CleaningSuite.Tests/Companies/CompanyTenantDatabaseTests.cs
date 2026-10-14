using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Infrastructure.Persistence;
using JasperFx;
using Marten;
using Marten.Newtonsoft;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using Newtonsoft.Json;
using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Commands;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Weasel.Core.Migrations;

namespace CleaningSuite.Tests.Companies;

public class CompanyTenantDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Legacy_global_index_migrates_without_changing_documents_and_is_idempotent()
    {
        var schema = "company_migration_" + Guid.NewGuid().ToString("N");
        var company = Company.Create("1234567-8", "Existing company");
        string originalJson;
        using (var legacy = LegacyStore(schema))
        {
            Assert.Contains("CREATE UNIQUE INDEX " + MartenConfig.CompanyBusinessIdIndexName,
                legacy.Storage.ToDatabaseScript());
            await legacy.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            using var session = legacy.LightweightSession("tenant-a");
            session.Store(company);
            await session.SaveChangesAsync();
            originalJson = JsonConvert.SerializeObject(company);
            using var foreign = legacy.LightweightSession("tenant-b");
            foreign.Store(Company.Create(company.BusinessId, "Rejected by legacy global index"));
            var error = await Assert.ThrowsAnyAsync<Exception>(() => foreign.SaveChangesAsync());
            Assert.True(IsUniqueViolation(error));
        }

        using var services = ProductionServices(schema);
        var store = services.GetRequiredService<IDocumentStore>();
        if (Environment.GetEnvironmentVariable("COMPANY_MIGRATION_SCRIPT_PATH") is { Length: > 0 } migrationPath)
            await store.Storage.Database.WriteMigrationFileAsync(migrationPath);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        using (var reader = store.QuerySession("tenant-a"))
            Assert.Equal(originalJson, JsonConvert.SerializeObject(await reader.LoadAsync<Company>(company.Id)));
        await Save(store, "tenant-b", Company.Create(company.BusinessId, "Other tenant company"));
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select indexname, indexdef from pg_indexes where schemaname = @schema and tablename = 'mt_doc_company'", connection);
        command.Parameters.AddWithValue("schema", schema);
        await using var indexes = await command.ExecuteReaderAsync();
        var businessIndexes = new List<(string Name, string Definition)>();
        while (await indexes.ReadAsync())
        {
            var definition = indexes.GetString(1);
            if (definition.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                && definition.Contains("BusinessId", StringComparison.Ordinal))
                businessIndexes.Add((indexes.GetString(0), definition));
        }
        var index = Assert.Single(businessIndexes);
        Assert.Equal(MartenConfig.CompanyBusinessIdIndexName, index.Name);
        Assert.Contains("tenant_id", index.Definition);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Foreign_company_cannot_be_used_as_a_branch_parent()
    {
        using var services = ProductionServices("company_parent_" + Guid.NewGuid().ToString("N"));
        var store = services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var foreign = Company.Create("FOREIGN-PARENT", "Foreign company");
        await Save(store, "tenant-b", foreign);
        using var tenant = Tenant(store, "tenant-a");
        var handler = new CompanyHandlers.BranchCommands.CreateBranchCommandHandler(
            new BranchRepository(tenant), new CompanyRepository(tenant));
        await Assert.ThrowsAsync<CleaningSuite.Application.Common.NotFoundException>(() =>
            handler.Handle(new CreateBranchCommand(foreign.Id, "Forbidden branch"), default));
        Assert.Empty(await tenant.Session.Query<Branch>().ToListAsync());
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.TenantId).Returns("tenant-a");
        var agreements = new Mock<IAgreementRepository>(MockBehavior.Strict);
        var files = new Mock<IAgreementFileStore>(MockBehavior.Strict);
        var agreementHandler = new CreateAgreementHandler(context.Object, agreements.Object, files.Object, new CompanyRepository(tenant));
        await Assert.ThrowsAsync<CleaningSuite.Application.Common.NotFoundException>(() => agreementHandler.Handle(
            new CreateAgreementCommand("Forbidden agreement", foreign.Id, Stream.Null, "a.pdf", [new("Alice", "alice@example.invalid")]), default));
        agreements.VerifyNoOtherCalls();
        files.VerifyNoOtherCalls();
        using var foreignReader = store.QuerySession("tenant-b");
        Assert.Empty(await foreignReader.Query<Branch>().ToListAsync());
        Assert.Equal(JsonConvert.SerializeObject(foreign), JsonConvert.SerializeObject(await foreignReader.LoadAsync<Company>(foreign.Id)));
    }

    [Fact]
    public async Task Company_conflict_response_is_409_with_safe_problem_details()
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        context.RequestServices = services.BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        var handler = new global::DefaultExceptionHandler(NullLogger<global::DefaultExceptionHandler>.Instance);
        Assert.True(await handler.TryHandleAsync(context, new CompanyConflictException(), default));
        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var problem = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(new CompanyConflictException().Message, problem.RootElement.GetProperty("detail").GetString());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Creates_updates_and_concurrent_creates_enforce_only_current_tenant_business_ids()
    {
        using var services = ProductionServices("company_scope_" + Guid.NewGuid().ToString("N"));
        var store = services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var own = Company.Create(" SAME-ID ", "Own company");
        var foreign = Company.Create("SAME-ID", "Foreign company");
        await Save(store, "tenant-a", own);
        await Save(store, "tenant-b", foreign);
        await Assert.ThrowsAsync<CompanyConflictException>(() => Save(store, "tenant-a", Company.Create("SAME-ID", "Duplicate")));

        var another = Company.Create("OTHER-ID", "Other company");
        await Save(store, "tenant-a", another);
        using (var tenant = Tenant(store, "tenant-a"))
        {
            var repository = new CompanyRepository(tenant);
            var loaded = (await repository.GetAsync(another.Id))!;
            loaded.Update("SAME-ID", loaded.Name, null, null, null, null, true);
            await Assert.ThrowsAsync<CompanyConflictException>(() => repository.SaveAsync(loaded));
        }
        using (var tenant = Tenant(store, "tenant-a"))
        {
            var repository = new CompanyRepository(tenant);
            Assert.Null(await repository.GetAsync(foreign.Id));
            var list = await repository.ListAsync(null, 0, 100, default);
            Assert.Equal(2, list.Total);
            var loaded = (await repository.GetAsync(own.Id))!;
            loaded.Update(loaded.BusinessId, "Updated own company", null, null, null, null, true);
            await repository.SaveAsync(loaded);
            Assert.Equal("OTHER-ID", (await repository.GetAsync(another.Id))!.BusinessId);
        }
        using (var tenant = Tenant(store, "tenant-b"))
        {
            var repository = new CompanyRepository(tenant);
            Assert.Null(await repository.GetAsync(own.Id));
            Assert.Equal(JsonConvert.SerializeObject(foreign), JsonConvert.SerializeObject(await repository.GetAsync(foreign.Id)));
        }

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            try { await Save(store, "tenant-a", Company.Create("RACE-ID", "Racer " + i)); return true; }
            catch (CompanyConflictException) { return false; }
        }));
        Assert.Single(outcomes.Where(x => x));
        using var reader = store.QuerySession("tenant-a");
        Assert.Single(await reader.Query<Company>().Where(x => x.BusinessId == "RACE-ID").ToListAsync());
    }

    private static string ConnectionString => Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;

    private static ServiceProvider ProductionServices(string schema)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(ConnectionString, databaseSchemaName: schema);
        return services.BuildServiceProvider();
    }

    private static IDocumentStore LegacyStore(string schema)
    {
        var options = new StoreOptions();
        options.RestoreV8Defaults();
        options.UseNewtonsoftForSerialization();
        options.Connection(ConnectionString);
        options.DatabaseSchemaName = schema;
        options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
        options.Policies.AllDocumentsAreMultiTenanted();
        options.Policies.ForAllDocuments(mapping => mapping.UseOptimisticConcurrency = true);
        options.Schema.For<Company>().UniqueIndex(x => x.BusinessId).Index(x => x.Name);
        return new DocumentStore(options);
    }

    private static TenantSessionFactory Tenant(IDocumentStore store, string tenantId)
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.TenantId).Returns(tenantId);
        return new TenantSessionFactory(store, context.Object);
    }

    private static async Task Save(IDocumentStore store, string tenantId, Company company)
    {
        using var tenant = Tenant(store, tenantId);
        await new CompanyRepository(tenant).SaveAsync(company);
    }

    private static bool IsUniqueViolation(Exception error) =>
        error is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        || error.InnerException is not null && IsUniqueViolation(error.InnerException);
}
