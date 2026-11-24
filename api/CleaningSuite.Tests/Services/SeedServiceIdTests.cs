using System.Text.RegularExpressions;

namespace CleaningSuite.Tests.Services;

// #393: seeded service documents must carry their own Id. Without it Marten generates a new GUID on each load,
// so the public services list changes on every request and booking rejects the ID it was given.
public class SeedServiceIdTests
{
    private static string SeedPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deploy", "seed-dev.sql"))) dir = dir.Parent;
        return dir is null ? throw new InvalidOperationException("deploy/seed-dev.sql not found") : Path.Combine(dir.FullName, "deploy", "seed-dev.sql");
    }

    [Fact]
    public void Every_seeded_service_document_repeats_its_row_id_in_its_json()
    {
        var sql = File.ReadAllText(SeedPath());
        var rows = Regex.Matches(sql, @"\('readysetsiivous', '([0-9a-f-]{36})', '\{(.*?)\}',", RegexOptions.Singleline);
        Assert.NotEmpty(rows);
        foreach (Match row in rows)
            Assert.Contains($"\"Id\":\"{row.Groups[1].Value}\"", "{" + row.Groups[2].Value + "}");
    }
}
