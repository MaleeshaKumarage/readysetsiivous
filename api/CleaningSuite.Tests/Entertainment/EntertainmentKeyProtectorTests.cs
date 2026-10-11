using System.Security.Cryptography;
using CleaningSuite.Infrastructure.Entertainment;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

public class EntertainmentKeyProtectorTests
{
    private static EntertainmentKeyProtector NewProtector()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entertainment:KeyEncryptionKey"] = key,
            })
            .Build();
        return new EntertainmentKeyProtector(config);
    }

    [Fact]
    public void Protect_ThenUnprotect_ReturnsOriginal()
    {
        var protector = NewProtector();
        const string secret = "sk-gemini-test-12345";

        var ciphertext = protector.Protect(secret);

        Assert.NotEqual(secret, ciphertext);
        Assert.Equal(secret, protector.Unprotect(ciphertext));
    }

    [Fact]
    public void Protect_SamePlaintextTwice_YieldsDifferentCiphertext()
    {
        var protector = NewProtector();
        var a = protector.Protect("same");
        var b = protector.Protect("same");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Constructor_MissingKey_Throws()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => new EntertainmentKeyProtector(config));
    }
}
