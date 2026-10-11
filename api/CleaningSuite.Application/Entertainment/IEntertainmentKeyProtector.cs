namespace CleaningSuite.Application.Entertainment;

/// <summary>Encrypts/decrypts the per-group Gemini API key at rest.</summary>
public interface IEntertainmentKeyProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
