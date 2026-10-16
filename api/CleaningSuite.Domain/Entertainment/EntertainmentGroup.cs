using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Entertainment;

public enum EntertainmentStatus
{
    Lobby,
    InGame,
    Finished,
}

/// <summary>
/// A reusable entertainment language-game group. Holds the topic description and
/// the creator-supplied Gemini API key (encrypted at rest). Reachable only by
/// its join key; not tenant-scoped — it lives in the fixed "entertainment"
/// partition.
/// </summary>
public class EntertainmentGroup : BaseDocument
{
    public string Name { get; set; } = string.Empty;
    public string GeminiApiKeyEncrypted { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string JoinKey { get; set; } = string.Empty;
    public EntertainmentStatus Status { get; set; } = EntertainmentStatus.Lobby;
    public List<Guid> TurnOrder { get; set; } = new();
    public Dictionary<Guid, int> Scores { get; set; } = new();
    public Guid? CurrentRoundId { get; set; }

    public static EntertainmentGroup Create(
        string name,
        string geminiApiKeyEncrypted,
        string description,
        string joinKey)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name is mandatory.", nameof(name));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is mandatory.", nameof(description));
        if (string.IsNullOrWhiteSpace(joinKey))
            throw new ArgumentException("Join key is mandatory.", nameof(joinKey));

        return new EntertainmentGroup
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            Name = name.Trim(),
            GeminiApiKeyEncrypted = geminiApiKeyEncrypted,
            Description = description.Trim(),
            JoinKey = joinKey,
            Status = EntertainmentStatus.Lobby,
        };
    }
}
