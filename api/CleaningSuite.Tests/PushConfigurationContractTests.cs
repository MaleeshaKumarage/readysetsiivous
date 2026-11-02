using CleaningSuite.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using WebPush;

namespace CleaningSuite.Tests;

public class PushConfigurationContractTests
{
    [Fact]
    public void Published_compose_environment_names_bind_to_available_settings()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "api", "CleaningSuite.Api", "appsettings.json"))) root = root.Parent;
        Assert.NotNull(root);
        var api = Path.Combine(root!.FullName, "api");
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(api, "CleaningSuite.Api", "appsettings.json")).Build();
        Assert.False(new PushSettings(config).Available);
        var keys = VapidHelper.GenerateVapidKeys();
        var variables = new Dictionary<string,string> {
            ["PUSH_NOTIFICATIONS_ENABLED"] = "true", ["PUSH_NOTIFICATIONS_VAPID_SUBJECT"] = "mailto:ops@example.test",
            ["PUSH_NOTIFICATIONS_VAPID_PUBLIC_KEY"] = keys.PublicKey, ["PUSH_NOTIFICATIONS_VAPID_PRIVATE_KEY"] = keys.PrivateKey };
        foreach (var compose in new[] { "docker-compose.yml", "docker-compose.dev.yml" })
        {
            var values = new Dictionary<string,string?>();
            foreach (var line in File.ReadAllLines(Path.Combine(api, "deploy", compose)).Where(x => x.TrimStart().StartsWith("PushNotifications__")))
            {
                var split = line.Trim().Split(": ", 2); var expression = split[1];
                var env = expression[2..expression.IndexOf(":-")];
                Assert.Contains(env + "=", File.ReadAllText(Path.Combine(api, "deploy", ".env.example")));
                values[split[0].Replace("__", ":")] = variables[env];
            }
            Assert.Equal(4, values.Count);
            Assert.True(new PushSettings(new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(values).Build()).Available);
        }
    }
}
