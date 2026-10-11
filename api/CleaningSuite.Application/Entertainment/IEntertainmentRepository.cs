using CleaningSuite.Domain.Entertainment;

namespace CleaningSuite.Application.Entertainment;

public interface IEntertainmentRepository
{
    /// <summary>Persists a new group and its creator player atomically.</summary>
    Task CreateGroupAsync(EntertainmentGroup group, EntertainmentPlayer creator, CancellationToken ct = default);

    Task SavePlayerAsync(EntertainmentPlayer player, CancellationToken ct = default);

    Task<bool> JoinKeyExistsAsync(string joinKey, CancellationToken ct = default);

    Task<EntertainmentGroup?> GetGroupByJoinKeyAsync(string joinKey, CancellationToken ct = default);

    Task<EntertainmentGroup?> GetGroupAsync(Guid groupId, CancellationToken ct = default);

    Task<EntertainmentPlayer?> GetPlayerAsync(Guid playerId, CancellationToken ct = default);

    Task<IReadOnlyList<EntertainmentPlayer>> ListPlayersAsync(Guid groupId, CancellationToken ct = default);
}
