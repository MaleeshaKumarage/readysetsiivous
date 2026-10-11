using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Entertainment;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Entertainment;

/// <summary>AES-GCM encryption for the per-group Gemini API key.</summary>
public class EntertainmentKeyProtector : IEntertainmentKeyProtector
{
    private readonly byte[] _key;

    public EntertainmentKeyProtector(IConfiguration configuration)
    {
        var keyBase64 = configuration["Entertainment:KeyEncryptionKey"];
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new InvalidOperationException(
                "Entertainment:KeyEncryptionKey is not configured. Set a 32-byte base64 key.");

        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length != 32)
            throw new InvalidOperationException(
                "Entertainment:KeyEncryptionKey must be a 32-byte base64 key.");
    }

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Layout: nonce (12) + tag (16) + ciphertext.
        var result = new byte[12 + 16 + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(tag, 0, result, 12, 16);
        Buffer.BlockCopy(cipherBytes, 0, result, 28, cipherBytes.Length);
        return Convert.ToBase64String(result);
    }

    public string Unprotect(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        var nonce = data[..12];
        var tag = data[12..28];
        var cipherBytes = data[28..];

        var plainBytes = new byte[cipherBytes.Length];
        using var aes = new AesGcm(_key);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
