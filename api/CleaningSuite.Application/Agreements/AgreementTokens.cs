using System.Security.Cryptography;

namespace CleaningSuite.Application.Agreements;

public static class AgreementTokens
{
    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
