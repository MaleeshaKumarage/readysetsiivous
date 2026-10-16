using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CleaningSuite.Application.Entertainment;

public static class EntertainmentSentenceIdentity
{
    public static string Normalize(string sentence) => string.Join(" ", Regex.Matches(sentence.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[\p{L}\p{N}]+") .Select(m => m.Value));
    public static string ReservationId(Guid groupId, string sentence) => $"{groupId:N}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(sentence))))}";
    public static bool IsDuplicate(string candidate, IEnumerable<string> history)
    {
        var normalized = Normalize(candidate);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return history.Any(previous =>
        {
            var other = Normalize(previous);
            if (other == normalized) return true;
            var oldTokens = other.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Reject a sentence template differing in only one short/numeric token.
            if (tokens.Length < 4 || tokens.Length != oldTokens.Length) return false;
            var differences = tokens.Zip(oldTokens).Where(p => p.First != p.Second).ToArray();
            return differences.Length == 1 && ((differences[0].First.Length <= 5 && differences[0].Second.Length <= 5) ||
                (differences[0].First.All(char.IsDigit) && differences[0].Second.All(char.IsDigit)));
        });
    }
}
