namespace CleaningSuite.Domain.Common;

/// <summary>Per-tenant sequence for booking/invoice numbers. Incremented in the same session as the create.</summary>
public class Counter : BaseDocument
{
    public string Name { get; set; } = "";
    public long Value { get; set; }
}
