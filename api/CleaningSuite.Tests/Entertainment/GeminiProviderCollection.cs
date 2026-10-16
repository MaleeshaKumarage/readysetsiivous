using Xunit;

namespace CleaningSuite.Tests.Entertainment;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GeminiProviderCollection
{
    public const string Name = "Gemini provider";
}
