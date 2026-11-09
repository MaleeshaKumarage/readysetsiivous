using System.Text.RegularExpressions;
using CleaningSuite.Infrastructure.Persistence;
using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace CleaningSuite.Tests.Support;

/// <summary>Owns exactly one GUID-suffixed fixture schema; disposal never scans or drops other schemas.</summary>
public sealed class TestDocumentStore(StoreOptions options, string connection) : DocumentStore(options), IAsyncDisposable
{
    private int _disposed;

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            DropOwnedSchema(connection, Options.DatabaseSchemaName);
        }
        finally { base.Dispose(); }
    }

    internal static void DropOwnedSchema(string connection, string schema)
    {
        if (!Regex.IsMatch(schema, "^[a-z][a-z0-9_]*_[a-f0-9]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Refusing to clean a schema that is not a uniquely named test fixture.");
        using var db = new NpgsqlConnection(connection);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
        command.ExecuteNonQuery();
    }

    public new ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}

// For migration tests that intentionally open several successive stores over the same schema.
public sealed class TestSchemaCleanup(string connection, string schema) : IDisposable
{
    public void Dispose() => TestDocumentStore.DropOwnedSchema(connection, schema);
}

public static class TestMartenRegistration
{
    public static IServiceCollection AddTestMarten(this IServiceCollection services, string connectionString,
        AutoCreate autoCreate = AutoCreate.CreateOrUpdate, string? databaseSchemaName = null)
    {
        services.AddCleaningMarten(connectionString, autoCreate, databaseSchemaName);
        services.Replace(ServiceDescriptor.Singleton<IDocumentStore>(provider =>
            new TestDocumentStore(provider.GetRequiredService<StoreOptions>(), connectionString)));
        return services;
    }
}
