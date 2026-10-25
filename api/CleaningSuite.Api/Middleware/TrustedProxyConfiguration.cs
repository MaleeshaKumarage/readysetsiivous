using Microsoft.AspNetCore.Builder;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace CleaningSuite.Api.Middleware;

/// <summary>Forwarded client identity is accepted only from explicitly configured peers.</summary>
public static class TrustedProxyConfiguration
{
    private static string[] Addresses(IConfigurationSection section, string name)
    {
        var array = section.GetSection(name).Get<string[]>() ?? [];
        var csv = section[name + "Csv"];
        if (string.IsNullOrEmpty(csv)) return array;
        if (array.Length != 0)
            throw new InvalidOperationException($"ReverseProxy:{name} and {name}Csv cannot both contain values.");
        var values = csv.Split(',').Select(value => value.Trim()).ToArray();
        if (values.Any(string.IsNullOrEmpty))
            throw new InvalidOperationException($"ReverseProxy:{name}Csv contains an empty address segment.");
        return values;
    }

    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection("ReverseProxy");
        var proxies = Addresses(section, "KnownProxies");
        var networks = Addresses(section, "KnownNetworks");
        var limit = section.GetValue("ForwardLimit", 1);
        if (limit is < 1 or > 10)
            throw new InvalidOperationException("ReverseProxy:ForwardLimit must be between 1 and 10.");
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.ForwardLimit = limit;
        // An empty trusted list in ASP.NET means trust everyone: disable processing instead.
        options.ForwardedHeaders = proxies.Length + networks.Length == 0
            ? ForwardedHeaders.None
            : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var value in proxies)
        {
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidOperationException("ReverseProxy:KnownProxies contains an invalid IP address.");
            options.KnownProxies.Add(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
        }
        foreach (var value in networks)
        {
            if (!System.Net.IPNetwork.TryParse(value, out var network) || network.PrefixLength == 0)
                throw new InvalidOperationException("ReverseProxy:KnownNetworks contains an invalid CIDR network or trust-all /0 network.");
            options.KnownIPNetworks.Add(network);
        }
    }
}
