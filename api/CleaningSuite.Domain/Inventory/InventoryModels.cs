using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Inventory;

/// <summary>
/// Inventory scope. Every inventory document carries an InventoryId so the feature can grow into
/// several inventories without a data migration. Only the default inventory exists today.
/// </summary>
public static class InventoryDefaults
{
    public const string InventoryId = "default";
}

/// <summary>A store or place items are taken from. Managed by admins.</summary>
public class InventoryLocation : BaseDocument
{
    public string InventoryId { get; set; } = InventoryDefaults.InventoryId;
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; NormalizedName = value.Trim().ToLowerInvariant(); }
    }

    /// <summary>Trimmed, lower-cased name. Unique per tenant, so concurrent creates cannot store the same name twice.</summary>
    public string NormalizedName { get; private set; } = "";
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A catalog item such as Keys or Bedsheets. Admin presets are verified. Items added by employees start
/// unverified: they are usable immediately, and an admin can verify, rename, or deactivate them.
/// </summary>
public class InventoryItem : BaseDocument
{
    public string InventoryId { get; set; } = InventoryDefaults.InventoryId;
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; NormalizedName = value.Trim().ToLowerInvariant(); }
    }

    /// <summary>Trimmed, lower-cased name. Unique per tenant, so concurrent creates cannot store the same name twice.</summary>
    public string NormalizedName { get; private set; } = "";
    public string Unit { get; set; } = "pcs";
    public bool IsVerified { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByEmployeeId { get; set; }
}

/// <summary>One log entry: an employee took some quantity of an item from a location, with at least one photo.</summary>
public class InventoryUsage : BaseDocument
{
    public string InventoryId { get; set; } = InventoryDefaults.InventoryId;
    public Guid EmployeeId { get; set; }
    /// <summary>Name snapshot at the time of logging, so history reads correctly after renames.</summary>
    public string EmployeeName { get; set; } = "";
    public Guid LocationId { get; set; }
    public string LocationName { get; set; } = "";
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "pcs";
    public int Quantity { get; set; }
    public string Note { get; set; } = "";
    /// <summary>Set by the server when the entry is saved. Employees cannot backdate entries.</summary>
    public DateTime TakenUtc { get; set; }
    public List<InventoryPhoto> Photos { get; set; } = new();
    /// <summary>Set when the retention job deletes the photos. The entry text is kept.</summary>
    public DateTime? PhotosPurgedUtc { get; set; }
}

/// <summary>A stored photo. The extension selects the file name and the served content type.</summary>
public sealed record InventoryPhoto(Guid Id, string Extension);
