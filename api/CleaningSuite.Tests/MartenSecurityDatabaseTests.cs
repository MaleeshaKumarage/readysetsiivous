using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Infrastructure.Persistence;
using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace CleaningSuite.Tests;

public class MartenSecurityDatabaseTests
{
    public class DictionaryDocument : BaseDocument
    {
        public Dictionary<string, string> Values { get; set; } = new();
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Production_mappings_preserve_documents_photos_versions_and_tenant_boundaries()
    {
        using var services = CreateServices();
        var store = services.GetRequiredService<IDocumentStore>();
        _ = store.Storage.ToDatabaseScript();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        var tenant = "security-" + Guid.NewGuid().ToString("N");
        var types = typeof(BaseDocument).Assembly.GetTypes().Where(t =>
            !t.IsAbstract && t.IsSubclassOf(typeof(BaseDocument))).ToArray();
        Assert.NotEmpty(types);
        foreach (var type in types)
        {
            var document = (BaseDocument)Activator.CreateInstance(type)!;
            if (document is QualityCycleForm form)
            {
                form.Photos = [new("data:image/png;base64,dGVzdA==", "Corridor cleaned")];
                form.CleanerNotes = "Notes without another photo";
            }
            using (var session = store.LightweightSession(tenant))
            {
                session.StoreObjects([document]);
                await session.SaveChangesAsync();
            }
            Assert.NotEqual(Guid.Empty, document.Version);
            using var reader = store.QuerySession(tenant);
            var loaded = (BaseDocument)(await Load(reader, type, document.Id))!;
            Assert.Equal(JsonConvert.SerializeObject(document), JsonConvert.SerializeObject(loaded));
            using var foreign = store.QuerySession(tenant + "-foreign");
            Assert.Null(await Load(foreign, type, document.Id));
        }
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Runtime_dictionary_keys_values_and_tenant_ids_cannot_inject_sql()
    {
        using var services = CreateServices();
        var store = services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "security-" + Guid.NewGuid().ToString("N");
        var own = new DictionaryDocument { Values = new() { ["area"] = "corridor" } };
        var other = new DictionaryDocument { Values = new() { ["area"] = "secret" } };
        using (var session = store.LightweightSession(tenant)) { session.Store(own); await session.SaveChangesAsync(); }
        using (var session = store.LightweightSession(tenant + "-foreign")) { session.Store(other); await session.SaveChangesAsync(); }
        using var query = store.QuerySession(tenant);
        var key = "area";
        Assert.Single(await query.Query<DictionaryDocument>().Where(x => x.Values.ContainsKey(key)).ToListAsync());
        foreach (var payload in new[] { "area') OR TRUE --", "area' OR 1=1 --", "x'; DROP TABLE mt_doc_dictionarydocument; --", "x\\'/* */" })
        {
            var projected = Assert.Single(await query.Query<DictionaryDocument>().Select(x => new { x.Id, Label = payload }).ToListAsync());
            Assert.Equal(own.Id, projected.Id);
            Assert.Equal(payload, projected.Label);
            key = payload;
            Assert.Empty(await query.Query<DictionaryDocument>().Where(x => x.Values.ContainsKey(key)).ToListAsync());
            Assert.Empty(await query.Query<DictionaryDocument>().Where(x => x.Values[key] == "secret").ToListAsync());
            var value = payload;
            Assert.Empty(await query.Query<DictionaryDocument>().Where(x => x.Values["area"] == value).ToListAsync());
            using var forgedTenant = store.QuerySession(payload);
            Assert.Null(await forgedTenant.LoadAsync<DictionaryDocument>(other.Id));
            Assert.Empty(await forgedTenant.Query<DictionaryDocument>().ToListAsync());
        }
        Assert.Single(await query.Query<DictionaryDocument>().ToListAsync());
        using var foreign = store.QuerySession(tenant + "-foreign");
        Assert.NotNull(await foreign.LoadAsync<DictionaryDocument>(other.Id));
    }

    private static Task<BaseDocument?> Load(IQuerySession session, Type type, Guid id) =>
        (Task<BaseDocument?>)typeof(MartenSecurityDatabaseTests).GetMethod(nameof(LoadTyped),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(type).Invoke(null, [session, id])!;

    private static async Task<BaseDocument?> LoadTyped<T>(IQuerySession session, Guid id) where T : BaseDocument =>
        await session.LoadAsync<T>(id);

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "security_" + Guid.NewGuid().ToString("N"));
        services.Configure<StoreOptions>(options =>
        {
            options.DatabaseSchemaName = "security_" + Guid.NewGuid().ToString("N");
            options.Schema.For<DictionaryDocument>();
        });
        return services.BuildServiceProvider();
    }
}
