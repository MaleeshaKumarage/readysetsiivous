using System.Security.Cryptography;

namespace CleaningSuite.Application.Agreements;

public static class AgreementTokens
{
    // Unambiguous alphabet: no 0/O, 1/I/L — safe to read aloud and type.
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string NewCode()
    {
        return string.Create(8, RandomNumberGenerator.GetBytes(8), (span, bytes) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        });
    }
}
