using CleaningSuite.Application.Invoicing;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
namespace CleaningSuite.Tests.Invoicing;
public class ShiftInvoiceCalculatorTests
{
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<Guid,string> Branches = new Dictionary<Guid,string> { [BranchId] = "AAA1" };
    private static Shift Shift() => new() { Id = Guid.NewGuid(), Name = "Biweekly", BranchId = BranchId, CompanyId = Guid.NewGuid(), IsActive = true, ValidFrom = new DateTime(2024,1,1) };
    private static ShiftBillingProfile Price(string mode, decimal rate, DateOnly from) => new() { Plans = [new(mode,rate,25.5m,from)] };
    private static List<InvoiceLine> Calculate(Shift s, ShiftBillingProfile p, DateOnly from, DateOnly to, IReadOnlyList<BillableShiftVisit>? visits = null, IReadOnlyList<MonthlyInvoiceOverride>? overrides = null) => ShiftInvoiceCalculator.Calculate(s,p,visits ?? [],from,to,TimeZoneInfo.Utc,Branches,overrides ?? []);
    [Theory]
    [InlineData(2026,10,17,31,150,72.58)]
    [InlineData(2024,2,15,29,290,150)]
    public void Monthly_rate_prorates_using_calendar_month_days(int year,int month,int start,int end,decimal rate,decimal expected)
    {
        var s=Shift(); var from=new DateOnly(year,month,start); var to=new DateOnly(year,month,end);
        var line=Assert.Single(Calculate(s,Price("Monthly",rate,new(year,month,1)),from,to));
        Assert.Equal(expected,line.NetAmount); Assert.Equal((decimal)(end-start+1)/DateTime.DaysInMonth(year,month),line.Quantity);
        Assert.Equal(decimal.Round(expected*.255m,2),line.VatAmount);
    }
    [Fact]
    public void Monthly_override_replaces_proration_and_keeps_explanation()
    {
        var s=Shift(); var lines=Calculate(s,Price("Monthly",150,new(2026,10,1)),new(2026,10,17),new(2026,10,31),overrides:[new(s.Id,"2026-10",100,"Agreed amount")]);
        var line=Assert.Single(lines);Assert.Equal(100,line.NetAmount);Assert.Equal("Agreed amount",line.OverrideReason);Assert.Equal(25.5m,line.VatAmount);
    }
    [Fact]
    public void Midmonth_price_change_uses_each_effective_price()
    {
        var s=Shift();var profile=Price("Monthly",310,new(2026,10,1));profile.Plans.Add(new("Monthly",620,25.5m,new(2026,10,17)));
        var lines=Calculate(s,profile,new(2026,10,1),new(2026,10,31));
        Assert.Equal(2,lines.Count);Assert.Equal(460,lines.Sum(l=>l.NetAmount));Assert.Contains(lines,l=>l.NetAmount==160);Assert.Contains(lines,l=>l.NetAmount==300);
    }
    [Fact]
    public void Monthly_override_cannot_hide_mixed_vat_rates()
    {
        var s=Shift();var profile=Price("Monthly",310,new(2026,10,1));profile.Plans.Add(new("Monthly",310,10,new(2026,10,17)));
        Assert.Throws<ValidationException>(()=>Calculate(s,profile,new(2026,10,1),new(2026,10,31),overrides:[new(s.Id,"2026-10",200,"Agreed") ]));
    }
    [Fact]
    public void Hourly_rate_uses_scheduled_hours_even_for_uncompleted_visits()
    {
        var s=Shift();var start=new DateTime(2026,10,2,8,0,0,DateTimeKind.Utc);
        var line=Assert.Single(Calculate(s,Price("Hourly",20,new(2026,10,1)),new(2026,10,1),new(2026,10,31),[new(start,start.AddHours(3),BranchId,s.Name,false)]));
        Assert.Equal(3,line.Hours);Assert.Equal(3,line.Quantity);Assert.Equal(60,line.NetAmount);Assert.Equal(1,line.Occurrences);
    }
    [Fact]
    public void Per_visit_bills_only_completed_occurrences_and_applies_individual_override()
    {
        var s=Shift();var start=new DateTime(2026,10,2,8,0,0,DateTimeKind.Utc);var p=Price("PerOccurrence",30,new(2026,10,1));p.Overrides.Add(new(start,45,"Extra work"));
        var lines=Calculate(s,p,new(2026,10,1),new(2026,10,31),[new(start,start.AddHours(2),BranchId,s.Name,true),new(start.AddDays(1),start.AddDays(1).AddHours(2),BranchId,s.Name,true),new(start.AddDays(2),start.AddDays(2).AddHours(2),BranchId,s.Name,false)]);
        Assert.Equal(75,lines.Sum(l=>l.NetAmount));Assert.Equal(2,lines.Sum(l=>l.Occurrences));Assert.Contains(lines,l=>l.OverrideReason=="Extra work"&&l.NetAmount==45);
    }
}
