namespace CleaningSuite.Tests.Scale;

/// <summary>Timed checks run alone, so other database tests cannot slow them and make the limits flaky.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScaleCollection
{
    public const string Name = "Scale timings";
}
