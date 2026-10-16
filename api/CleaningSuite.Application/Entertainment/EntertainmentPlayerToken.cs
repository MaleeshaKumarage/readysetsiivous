using System.Security.Cryptography;
using System.Text;

namespace CleaningSuite.Application.Entertainment;

public static class EntertainmentPlayerToken
{
    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static bool Verify(string? token, string? hash)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64 || string.IsNullOrEmpty(hash)) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(token)), Convert.FromHexString(hash));
        }
        catch (FormatException) { return false; }
    }
}
