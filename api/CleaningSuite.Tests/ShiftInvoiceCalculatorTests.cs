using System.Linq;
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
    // Monthly fee = monthly rate x completed visits / scheduled visits in the month (the shift's active days only).
    private static BillableShiftVisit MonthlyVisit(DateTime day, bool completed) => new(day.AddHours(8), day.AddHours(10), BranchId, "Biweekly", completed);
    [Theory]
    [InlineData(4,2,75)]
    [InlineData(5,3,90)]
    [InlineData(5,2,60)]
    [InlineData(4,4,150)]
    [InlineData(4,0,0)]
    public void Monthly_fee_follows_completed_visits_over_scheduled_visits(int scheduled,int completed,decimal expected)
    {
        var visits=Enumerable.Range(0,scheduled).Select(i=>MonthlyVisit(new DateTime(2026,10,1+i*7),i<completed)).ToList();
        var line=Assert.Single(Calculate(Shift(),Price("Monthly",150,new(2026,10,1)),new(2026,10,1),new(2026,10,31),visits));
        Assert.Equal(expected,line.NetAmount);
        Assert.Equal((decimal)completed/scheduled,line.Quantity);
        Assert.Equal(Math.Round(expected*.255m,2,MidpointRounding.AwayFromZero),line.VatAmount);
    }
    [Fact]
    public void Shift_ending_mid_month_is_billed_only_for_the_visits_it_covered()
    {
        // The shift ends on 22 Oct, so the 29 Oct visit is outside its active days and does not count.
        var shift=Shift();shift.ValidUntil=new(2026,10,22);
        var visits=new[]{MonthlyVisit(new DateTime(2026,10,1),true),MonthlyVisit(new DateTime(2026,10,8),true),MonthlyVisit(new DateTime(2026,10,15),false),MonthlyVisit(new DateTime(2026,10,22),false),MonthlyVisit(new DateTime(2026,10,29),true)};
        var line=Assert.Single(Calculate(shift,Price("Monthly",150,new(2026,10,1)),new(2026,10,1),new(2026,10,31),visits));
        Assert.Equal(75m,line.NetAmount);
        Assert.Equal(4,line.Occurrences);
    }
    [Fact]
    public void Partial_selection_bills_the_completed_share_of_the_visits_it_covers()
    {
        var shift=Shift();shift.ValidFrom=new(2026,10,2);
        var visits=new[]{MonthlyVisit(new DateTime(2026,10,2),true),MonthlyVisit(new DateTime(2026,10,9),true),MonthlyVisit(new DateTime(2026,10,16),true),MonthlyVisit(new DateTime(2026,10,23),false),MonthlyVisit(new DateTime(2026,10,30),false)};
        var line=Assert.Single(Calculate(shift,Price("Monthly",150,new(2026,10,1)),new(2026,10,2),new(2026,10,31),visits));
        Assert.Equal(90m,line.NetAmount);
    }
    [Fact]
    public void Partial_invoices_for_one_month_add_up_to_the_full_month_fee()
    {
        // 4 visits in October, 2 completed (both in the first half). A full-month invoice bills 2/4 of 150 = 75.
        var all=new[]{MonthlyVisit(new DateTime(2026,10,1),true),MonthlyVisit(new DateTime(2026,10,8),true),MonthlyVisit(new DateTime(2026,10,15),false),MonthlyVisit(new DateTime(2026,10,22),false)};
        var shift=Shift();var price=Price("Monthly",150,new(2026,10,1));
        var full=Assert.Single(ShiftInvoiceCalculator.Calculate(shift,price,all,new(2026,10,1),new(2026,10,31),TimeZoneInfo.Utc,Branches,[],null,null,all));
        Assert.Equal(75m,full.NetAmount);
        // Split into two invoices. Each invoice gets the visits in its window, but the fee is divided by all October visits.
        var firstHalf=all.Where(v=>v.StartUtc.Day<=15).ToArray();
        var secondHalf=all.Where(v=>v.StartUtc.Day>15).ToArray();
        var a=Assert.Single(ShiftInvoiceCalculator.Calculate(shift,price,firstHalf,new(2026,10,1),new(2026,10,15),TimeZoneInfo.Utc,Branches,[],null,null,all));
        var b=Assert.Single(ShiftInvoiceCalculator.Calculate(shift,price,secondHalf,new(2026,10,16),new(2026,10,31),TimeZoneInfo.Utc,Branches,[],null,null,all));
        Assert.Equal(75m,a.NetAmount);
        Assert.Equal(0m,b.NetAmount);
        Assert.Equal(full.NetAmount,a.NetAmount+b.NetAmount);
    }
    [Fact]
    public void Full_month_with_no_active_days_does_not_charge()
    {
        var shift=Shift();shift.ValidFrom=new(2026,11,1);
        Assert.Empty(Calculate(shift,Price("Monthly",150,new(2026,10,1)),new(2026,10,1),new(2026,10,31)));
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
        var visits=new[]{MonthlyVisit(new DateTime(2026,10,1),true),MonthlyVisit(new DateTime(2026,10,8),false),MonthlyVisit(new DateTime(2026,10,22),true),MonthlyVisit(new DateTime(2026,10,29),true)};
        var lines=Calculate(s,profile,new(2026,10,1),new(2026,10,31),visits);
        // Before 17 Oct: 310 x 1/2 = 155. From 17 Oct: 620 x 2/2 = 620.
        Assert.Equal(2,lines.Count);Assert.Equal(775,lines.Sum(l=>l.NetAmount));Assert.Contains(lines,l=>l.NetAmount==155);Assert.Contains(lines,l=>l.NetAmount==620);
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
