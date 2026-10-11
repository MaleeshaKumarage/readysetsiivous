using CleaningSuite.Domain.Entertainment;

namespace CleaningSuite.Application.Entertainment;

public interface IEntertainmentRepository
{
    /// <summary>Persists a new group and its creator player atomically.</summary>
    Task CreateGroupAsync(EntertainmentGroup group, EntertainmentPlayer creator, CancellationToken ct = default);

    Task SavePlayerAsync(EntertainmentPlayer player, CancellationToken ct = default);

    Task SaveGroupAsync(EntertainmentGroup group, CancellationToken ct = default);

    Task SaveRoundAsync(EntertainmentRound round, CancellationToken ct = default);

    Task<EntertainmentRound?> GetRoundAsync(Guid roundId, CancellationToken ct = default);

    Task<bool> JoinKeyExistsAsync(string joinKey, CancellationToken ct = default);

    Task<EntertainmentGroup?> GetGroupByJoinKeyAsync(string joinKey, CancellationToken ct = default);

    Task<EntertainmentGroup?> GetGroupAsync(Guid groupId, CancellationToken ct = default);

    Task<EntertainmentPlayer?> GetPlayerAsync(Guid playerId, CancellationToken ct = default);

    Task<IReadOnlyList<EntertainmentPlayer>> ListPlayersAsync(Guid groupId, CancellationToken ct = default);

    Task<bool> SentenceExistsAsync(Guid groupId, string sentenceEn, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListRecentSentencesAsync(Guid groupId, int count, CancellationToken ct = default);

    Task<IReadOnlyList<EntertainmentGroup>> ListInactiveGroupsAsync(DateTime cutoffUtc, CancellationToken ct = default);

    Task DeleteGroupAsync(Guid groupId, CancellationToken ct = default);
}
