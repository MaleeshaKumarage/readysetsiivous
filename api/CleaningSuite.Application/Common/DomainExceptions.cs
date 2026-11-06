namespace CleaningSuite.Application.Common;

/// <summary>Slug already taken by another document in the tenant.</summary>
public class SlugConflictException(string slug)
    : Exception($"Slug '{slug}' already exists. Change the service name or slug so it is unique, then save again.");

/// <summary>Requested document does not exist.</summary>
public class NotFoundException(string entity, Guid id)
    : Exception($"{entity} {id} not found")
{
    /// <summary>The kind of record that was not found, for example "Company". Used to write a message without the id.</summary>
    public string Entity { get; } = entity;
}

/// <summary>A public booking lookup or cancel that does not match a booking number and management key. Explains the next step.</summary>
public class PublicBookingNotFoundException() : Exception(GuidanceText)
{
    public const string GuidanceText = "No booking matches these details. Check the booking reference and the management code in your confirmation email, then try again.";
}

/// <summary>A public availability lookup for a service that no longer exists. Explains the next step without the internal id.</summary>
public class PublicServiceNotOfferedException() : Exception(GuidanceText)
{
    public const string GuidanceText = "That service is no longer offered. Choose another service, then try again.";
}

/// <summary>A public signing link that matches no agreement or signer. Explains the next step.</summary>
public class PublicSigningLinkNotFoundException() : Exception(GuidanceText)
{
    public const string GuidanceText = "This signing link is not valid or has been replaced. Ask the sender for a new signing link.";
}
