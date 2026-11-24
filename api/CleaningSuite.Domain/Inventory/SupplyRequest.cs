using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Inventory;

/// <summary>Where a supply request is in its life. Requested → Approved → Fulfilled, or Requested → Declined.</summary>
public enum SupplyRequestStatus
{
    Requested,
    Approved,
    Fulfilled,
    Declined,
}

/// <summary>One item on a supply request. The name and unit are copied from the catalogue when the request is made.</summary>
public sealed record SupplyRequestLine(Guid ItemId, string ItemName, string Unit, int Quantity);

/// <summary>A request from an employee for supplies from the catalogue. Admins and managers approve, decline and fulfil it.</summary>
public class SupplyRequest : BaseDocument
{
    public string InventoryId { get; set; } = InventoryDefaults.InventoryId;
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public List<SupplyRequestLine> Lines { get; set; } = [];
    public string Note { get; set; } = "";
    public SupplyRequestStatus Status { get; set; } = SupplyRequestStatus.Requested;
    public string? DecisionNote { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedUtc { get; set; }
}
