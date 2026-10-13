using CleaningSuite.Application.Entertainment;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class EntertainmentJoinKeyTests
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    [Fact]
    public void Generate_ReturnsRequestedLength()
    {
        var key = EntertainmentJoinKey.Generate(6);
        Assert.Equal(6, key.Length);
    }

    [Fact]
    public void Generate_UsesOnlyUnambiguousAlphabet()
    {
        for (var i = 0; i < 1000; i++)
        {
            var key = EntertainmentJoinKey.Generate();
            foreach (var c in key)
                Assert.Contains(c, Alphabet);
        }
    }

    [Fact]
    public void Generate_ProducesVariety()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
            seen.Add(EntertainmentJoinKey.Generate());

        Assert.True(seen.Count > 100, $"Expected many distinct keys, got {seen.Count}.");
    }
}
