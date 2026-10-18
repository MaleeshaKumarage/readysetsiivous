using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Invoicing;

/// <summary>Admin-only pricing, deliberately separate from work and quality documents.</summary>
public class ShiftBillingProfile : BaseDocument
{
    public Guid ShiftId { get; set; }
    public List<ShiftPricingPlan> Plans { get; set; } = [];
    public List<ShiftOccurrencePrice> Overrides { get; set; } = [];
}

public record ShiftPricingPlan(string Mode, decimal RateNet, decimal VatRatePercent, DateOnly EffectiveFrom);
public record ShiftOccurrencePrice(DateTime OccurrenceStartUtc, decimal RateNet, string Reason);
