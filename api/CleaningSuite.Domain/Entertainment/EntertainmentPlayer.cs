using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Entertainment;

public enum CefrLevel
{
    A1,
    A2,
    B1,
    B2,
    C1,
    C2,
}

/// <summary>
/// A participant in an entertainment group. The creator is the first player
/// (IsCreator = true). Score is reveal-count driven, updated in-memory during a
/// game and persisted to the round log.
/// </summary>
public class EntertainmentPlayer : BaseDocument
{
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CefrLevel Level { get; set; }
    public int Score { get; set; }
    public bool IsCreator { get; set; }
    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;

    public static EntertainmentPlayer Create(
        Guid groupId,
        string name,
        CefrLevel level,
        bool isCreator)
    {
        if (groupId == Guid.Empty)
            throw new ArgumentException("Group id is mandatory.", nameof(groupId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Player name is mandatory.", nameof(name));

        return new EntertainmentPlayer
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            GroupId = groupId,
            Name = name.Trim(),
            Level = level,
            Score = 0,
            IsCreator = isCreator,
            JoinedUtc = DateTime.UtcNow,
        };
    }
}
