using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Entertainment;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Entertainment;

/// <summary>AES-GCM encryption for the per-group Gemini API key.</summary>
/// <remarks>
/// The 32-byte encryption key is resolved in order:
/// 1. <c>Entertainment:KeyEncryptionKey</c> (explicit override).
/// 2. A persisted key file at <c>Entertainment:KeyFilePath</c> (default "entertainment.key").
/// 3. Generated on first use and written to that file, so it stays stable across
///    restarts without any manual provisioning step.
/// </remarks>
public class EntertainmentKeyProtector : IEntertainmentKeyProtector
{
    private readonly byte[] _key;

    public EntertainmentKeyProtector(IConfiguration configuration)
    {
        _key = LoadOrCreateKey(configuration);
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

    private static byte[] LoadOrCreateKey(IConfiguration configuration)
    {
        var explicitKey = configuration["Entertainment:KeyEncryptionKey"];
        if (!string.IsNullOrWhiteSpace(explicitKey))
            return Validate(Convert.FromBase64String(explicitKey));

        var path = configuration["Entertainment:KeyFilePath"] ?? "entertainment.key";
        if (File.Exists(path))
            return Validate(Convert.FromBase64String(File.ReadAllText(path).Trim()));

        var key = RandomNumberGenerator.GetBytes(32);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, Convert.ToBase64String(key));
        return key;
    }

    private static byte[] Validate(byte[] key)
    {
        if (key.Length != 32)
            throw new InvalidOperationException("Entertainment encryption key must be 32 bytes.");
        return key;
    }
}
