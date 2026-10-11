namespace CleaningSuite.Application.Entertainment;

/// <summary>Generates short human-friendly join keys with an unambiguous alphabet.</summary>
public static class EntertainmentJoinKey
{
    // No 0/O, 1/I/L to avoid look-alike confusion.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate(int length = 6)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];
        return new string(chars);
    }
}
