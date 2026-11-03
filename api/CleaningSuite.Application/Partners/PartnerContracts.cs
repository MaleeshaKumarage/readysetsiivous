using Marten;

namespace CleaningSuite.Application.Partners;

/// <summary>Always reads the current request's tenant session, including after reservation resets.</summary>
public interface IPartnerStore { IDocumentSession Session { get; } }

public record CreatePartnerBranch(string Name = "", string? Street = null, string? PostalCode = null,
    string? City = null, string? Country = null, string? ContactPhone = null, string? ContactEmail = null,
    int? RoomCount = null, int? BedCount = null, decimal? SizeSquareMeters = null);
public record CreatePartnerRequest(Guid ClientRequestId, Guid BranchId, DateOnly FromDate, DateOnly ToDate,
    string? ExactStartLocalTime = null, string? Notes = null);
public record EditPartnerRequest(int Version, DateOnly FromDate, DateOnly ToDate,
    string? ExactStartLocalTime = null, string? Notes = null);
public record PartnerVersion(int Version);
public record PartnerMessage(string Message);
public record PartnerResolution(string Resolution);
public record PartnerPricing(decimal PriceNet, decimal VatRatePercent, int EstimatedDurationMinutes);
public record PartnerOffer(int Version, Guid EmployeeId, string? PlannedStartLocal = null);
public record PartnerAcceptance(int Version, string? PlannedStartLocal = null);
public record PartnerDecline(int Version, string? Reason = null);
public record PartnerCancellation(int Version, string Reason);
public record PartnerRequestDto(Guid Id, int Version, Guid CompanyId, Guid BranchId, string BranchName,
    DateOnly FromDate, DateOnly ToDate, string? ExactStartLocalTime, string? Notes, decimal PriceNet,
    decimal VatRatePercent, int EstimatedDurationMinutes, string Status, DateTime? FirstAcceptedAtUtc,
    Guid? LinkedShiftId, DateTime? PlannedStartUtc, DateTime? PlannedEndUtc, string? AcceptedCleanerName,
    DateTime? ActualStartUtc, DateTime? ActualEndUtc, Guid? OfferedEmployeeId = null, Guid? OfferId = null);
public record PartnerChangeDto(Guid Id, Guid CompanyId, Guid BranchId, Guid? RequestId, string Kind,
    string Message, DateTime CreatedUtc, DateTime? ResolvedUtc, string? Resolution);
public record PartnerCleanerOfferDto(Guid Id, Guid RequestId, int Version, string BranchName,
    string? Street, string? PostalCode, string? City, DateOnly FromDate, DateOnly ToDate,
    string? ExactStartLocalTime, DateTime? PlannedStartUtc, int EstimatedDurationMinutes,
    string? Notes, string TimeZoneId);
