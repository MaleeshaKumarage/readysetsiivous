using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Entertainment;

public sealed record ProviderKey(string Provider, string Slot, string Value);

public static class EntertainmentAiKeyPool
{
    public static IReadOnlyList<ProviderKey> Gemini(IConfiguration config)
    {
        var indexed = ReadIndexed(config, "Entertainment:Gemini:Keys");
        var legacy = new[] { config["Entertainment:Gemini:ApiKey"], config["GEMINI_KEY"], config["GEMINI_API_KEY"], config["GAME_GEMINI_API_KEY"] }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select((x, i) => new ProviderKey("Gemini", $"legacy-{i + 1}", x!.Trim()));
        return Distinct(indexed.Concat(legacy));
    }

    public static IReadOnlyList<ProviderKey> OpenAi(IConfiguration config) =>
        Distinct(ReadIndexed(config, "Entertainment:OpenAI:Keys"));

    private static IEnumerable<ProviderKey> ReadIndexed(IConfiguration config, string path)
    {
        return config.GetSection(path).GetChildren()
            .Select(section => (section.Key, section.Value))
            .Where(x => int.TryParse(x.Key, out var slot) && slot > 0 && !string.IsNullOrWhiteSpace(x.Value))
            .OrderBy(x => int.Parse(x.Key))
            .Select(x => new ProviderKey(path.Contains("Gemini", StringComparison.Ordinal) ? "Gemini" : "OpenAI", x.Key, x.Value!.Trim()));
    }

    private static IReadOnlyList<ProviderKey> Distinct(IEnumerable<ProviderKey> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return keys.Where(k => seen.Add(k.Value)).ToArray();
    }
}
