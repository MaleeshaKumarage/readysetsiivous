using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Inventory;

/// <summary>One immutable inventory snapshot shared with one company. Id equals the usage id,
/// preventing a second disclosure/request for the same entry, including concurrent batches.</summary>
public class InventoryAcknowledgment : BaseDocument
{
    public Guid BatchId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public string RequestedBySubject { get; set; } = "";
    public string Message { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public int Quantity { get; set; }
    public string Unit { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime TakenUtc { get; set; }
    public List<Guid> PhotoIds { get; set; } = [];
    public string Status { get; set; } = "Pending";
    public string ResponseNote { get; set; } = "";
    public Guid? RespondedByEmployeeId { get; set; }
    public string? RespondedByName { get; set; }
    public DateTime? RespondedUtc { get; set; }
}
