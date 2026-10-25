using System.Security.Cryptography;
using System.Text;

namespace CleaningSuite.Application.Bookings;

/// <summary>Canonical uppercase hex keys; only their digest belongs in persistence.</summary>
public static class BookingManagementKey
{
    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static bool Verify(string? key, string? hash)
    {
        if (!IsCanonicalHex(key) || !IsCanonicalHex(hash)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(Hash(key!)), Convert.FromHexString(hash!));
    }

    private static bool IsCanonicalHex(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
