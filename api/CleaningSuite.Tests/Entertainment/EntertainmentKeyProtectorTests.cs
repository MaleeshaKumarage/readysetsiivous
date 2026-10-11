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
    public void MissingKey_AutoGeneratesAndPersists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ent-key-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "entertainment.key");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Entertainment:KeyFilePath"] = path,
                })
                .Build();

            var protector = new EntertainmentKeyProtector(config);
            Assert.True(File.Exists(path));

            const string secret = "sk-secret";
            var ciphertext = protector.Protect(secret);
            Assert.Equal(secret, protector.Unprotect(ciphertext));

            // A new instance reloads the persisted key, so decryption still works.
            var reloaded = new EntertainmentKeyProtector(config);
            Assert.Equal(secret, reloaded.Unprotect(ciphertext));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WrongLengthExplicitKey_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entertainment:KeyEncryptionKey"] = Convert.ToBase64String(new byte[16]),
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new EntertainmentKeyProtector(config));
    }
}
