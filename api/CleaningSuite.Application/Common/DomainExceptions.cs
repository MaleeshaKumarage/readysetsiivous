namespace CleaningSuite.Application.Common;

/// <summary>Slug already taken by another document in the tenant.</summary>
public class SlugConflictException(string slug)
    : Exception($"Slug '{slug}' already exists");

/// <summary>Requested document does not exist.</summary>
public class NotFoundException(string entity, Guid id)
    : Exception($"{entity} {id} not found");

/// <summary>Generic business-rule conflict (maps to 409).</summary>
public class ConflictException(string message) : Exception(message);
