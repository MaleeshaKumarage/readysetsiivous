namespace CleaningSuite.Application.Entertainment;

/// <summary>No group exists with the given join key.</summary>
public class EntertainmentGroupNotFoundException(string joinKey)
    : Exception($"No group with join key '{joinKey}'.");

/// <summary>The group has already started; joining is closed.</summary>
public class EntertainmentJoinClosedException(string joinKey)
    : Exception($"Group '{joinKey}' has already started; joining is closed.");

/// <summary>An attempt was made to start a group that is not in the Lobby state.</summary>
public class EntertainmentGameAlreadyStartedException(Guid groupId)
    : Exception($"Group {groupId} has already started.");
