namespace CleaningSuite.Tests.Support;

// Fixture schemas isolate documents, but reservation advisory locks belong to the database, not the schema.
// Keep separate fixtures apart; concurrency inside each race test still runs against real connections.
[CollectionDefinition("Database integration", DisableParallelization = true)]
public sealed class DatabaseIntegrationCollection;
