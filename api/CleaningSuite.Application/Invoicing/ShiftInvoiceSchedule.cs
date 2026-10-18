using System.Text.Json;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Invoicing;

/// <summary>Late data entry must not omit contracted visits before the record was created.</summary>
public static class ShiftInvoiceSchedule
{
    public static Shift Snapshot(Shift source, TimeZoneInfo zone)
    {
        // Never mutate the tracked work/QC document while calculating an invoice.
        var shift = JsonSerializer.Deserialize<Shift>(JsonSerializer.Serialize(source))!;
        if (shift.QualityCyclePlans.Count == 0 && shift.ValidFrom.HasValue)
        {
            var beginning = TenantTimeZone.StartOfLocalDay(shift.ValidFrom.Value, zone);
            if (beginning < shift.CreatedUtc) shift.CreatedUtc = beginning;
        }
        var first = shift.QualityCyclePlans.OrderBy(p => p.EffectiveFromUtc).FirstOrDefault();
        if (first is { IsActive: true, ValidFrom: not null })
        {
            var beginning = TenantTimeZone.StartOfLocalDay(first.ValidFrom.Value, zone);
            if (beginning < first.EffectiveFromUtc) first.EffectiveFromUtc = beginning;
        }
        return shift;
    }
}
