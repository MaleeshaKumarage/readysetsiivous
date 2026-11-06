namespace CleaningSuite.Application.Common;

/// <summary>Slug already taken by another document in the tenant.</summary>
public class SlugConflictException(string slug)
    : Exception($"Slug '{slug}' already exists. Change the service name or slug so it is unique, then save again.");

/// <summary>Requested document does not exist.</summary>
public class NotFoundException(string entity, Guid id)
    : Exception($"{entity} {id} not found");
