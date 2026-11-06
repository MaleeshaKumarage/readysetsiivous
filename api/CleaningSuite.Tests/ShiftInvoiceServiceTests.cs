using System.Linq;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using FluentValidation;
using Moq;
namespace CleaningSuite.Tests.Invoicing;
public class ShiftInvoiceServiceTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Backdated_shift_bills_scheduled_visits_before_data_entry_without_changing_work_history(bool history)
    {
        var f = new Fixture();
        f.Shift.CreatedUtc = new(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        if (history) f.Shift.RecordQualityCyclePlan(f.Shift.CreatedUtc);
        f.Pricing.Plans = [new("Hourly", 50, 25.5m, new(2026, 10, 1))];
        var preview = await f.Service.PreviewAsync(f.Request, default);
        var line = Assert.Single(preview.Invoice.Lines);
        Assert.Equal(5, line.Occurrences); Assert.Equal(10, line.Hours); Assert.Equal(500, line.NetAmount);
        Assert.Equal(new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc), f.Shift.CreatedUtc);
        if (history) Assert.Equal(f.Shift.CreatedUtc, f.Shift.QualityCyclePlans[0].EffectiveFromUtc);
        else Assert.Empty(f.Shift.QualityCyclePlans);
    }
    [Fact]
    public async Task Inverted_billing_period_reports_the_date_order_not_the_length_limit()
    {
        var f = new Fixture();
        var inverted = f.Request with { From = f.Request.To, To = f.Request.From };
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Service.PreviewAsync(inverted, default));
        var message = Assert.Single(ex.Errors).ErrorMessage;
        Assert.Equal("The billing period must end on or after its start date.", message);
    }

    [Fact]
    public async Task New_year_number_uses_the_tenant_local_issue_year_for_both_counter_and_prefix()
    {
        var f = new Fixture(new DateTimeOffset(2026, 12, 31, 22, 30, 0, TimeSpan.Zero));
        f.Invoices.Setup(x => x.NextInvoiceNumberForYearAsync(2027, It.IsAny<CancellationToken>())).ReturnsAsync(1L);
        var preview = await f.Service.PreviewAsync(f.Request, default);
        var invoice = await f.Service.IssueAsync(f.Request with { PreviewToken = preview.PreviewToken }, default);
        Assert.Equal(new DateTime(2027, 1, 1), invoice.IssueDate);
        Assert.Equal("2027-0001", invoice.InvoiceNumber);
        f.Invoices.Verify(x => x.NextInvoiceNumberForYearAsync(2027, It.IsAny<CancellationToken>()), Times.Once);
        f.Invoices.Verify(x => x.NextInvoiceNumberForYearAsync(2026, It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task Preview_is_stable_without_writes_and_issue_uses_server_totals_once()
    {
        var f=new Fixture();var first=await f.Service.PreviewAsync(f.Request,default);var second=await f.Service.PreviewAsync(f.Request,default);
        Assert.Equal(first.PreviewToken,second.PreviewToken);Assert.Equal(150,first.Invoice.Total.Net);Assert.Equal(Invoice.StatusDraft,first.Invoice.Status);
        f.Invoices.Verify(x=>x.SaveAsync(It.IsAny<Invoice>(),It.IsAny<CancellationToken>()),Times.Never);
        f.Invoices.Verify(x=>x.NextInvoiceNumberForYearAsync(2026, It.IsAny<CancellationToken>()),Times.Never);
        var issued=await f.Service.IssueAsync(f.Request with { PreviewToken=first.PreviewToken },default);
        Assert.Equal("2026-0007",issued.InvoiceNumber);Assert.Equal(Invoice.StatusIssued,issued.Status);Assert.NotEqual(Guid.Empty,issued.Id);
        Assert.Equal(ShiftInvoiceService.PaymentReference("20260007"),issued.PaymentReference);
        f.Invoices.Verify(x=>x.SaveAsync(issued,It.IsAny<CancellationToken>()),Times.Once);
        f.Invoices.Verify(x=>x.NextInvoiceNumberForYearAsync(2026, It.IsAny<CancellationToken>()),Times.Once);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Changed_price_or_issuer_invalidates_preview(bool price)
    {
        var f=new Fixture();var preview=await f.Service.PreviewAsync(f.Request,default);
        if(price)f.Pricing.Plans=[new("Monthly",200,25.5m,new(2026,10,1))];else f.Profile.CompanyName="Changed issuer";
        await Assert.ThrowsAsync<ShiftConflictException>(()=>f.Service.IssueAsync(f.Request with { PreviewToken=preview.PreviewToken },default));
        f.Invoices.Verify(x=>x.SaveAsync(It.IsAny<Invoice>(),It.IsAny<CancellationToken>()),Times.Never);
    }
    [Theory][InlineData("Issued",false,true)][InlineData("Paid",false,true)][InlineData("Voided",false,false)][InlineData("Issued",true,false)]
    public async Task Existing_overlap_blocks_issued_and_paid_but_not_void_or_legacy_booking(string status,bool legacy,bool blocked)
    {
        var f=new Fixture();f.Existing.Add(new() { Status=status,CompanyId=legacy?null:f.Company.Id,PeriodFrom=new(2026,10,15),PeriodTo=new(2026,11,15),Lines=[new(){ShiftId=legacy?null:f.Shift.Id}] });
        if(blocked)await Assert.ThrowsAsync<ShiftConflictException>(()=>f.Service.PreviewAsync(f.Request,default));
        else Assert.NotEmpty((await f.Service.PreviewAsync(f.Request,default)).Invoice.Lines);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Missing_or_foreign_company_shift_is_rejected(bool missing)
    {
        var f=new Fixture();if(missing)f.Selected.Clear();else f.Shift.CompanyId=Guid.NewGuid();
        await Assert.ThrowsAsync<ValidationException>(()=>f.Service.PreviewAsync(f.Request,default));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Issue_requires_complete_billing_address_and_valid_issuer(bool issuer)
    {
        var f=new Fixture();var request=f.Request;
        if(issuer)f.Profile.BankAccountIBAN="INVALID";else request=request with { BillingAddress=new() { Street="Street only" } };
        var preview=await f.Service.PreviewAsync(request,default);
        await Assert.ThrowsAsync<ValidationException>(()=>f.Service.IssueAsync(request with { PreviewToken=preview.PreviewToken },default));
        f.Invoices.Verify(x=>x.NextInvoiceNumberForYearAsync(2026, It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task Blank_billing_address_is_named_even_when_the_preview_token_is_stale()
    {
        var f=new Fixture();
        var request=f.Request with { BillingAddress=new() { Street="", PostalCode="", City="", Country="FI" } };
        var ex=await Assert.ThrowsAsync<ValidationException>(()=>f.Service.IssueAsync(request with { PreviewToken="stale-token" },default));
        Assert.Contains("billing street", ex.Message);
        f.Invoices.Verify(x=>x.NextInvoiceNumberForYearAsync(2026, It.IsAny<CancellationToken>()),Times.Never);
    }
    [Fact]
    public async Task Issue_without_preview_is_rejected()
    {
        var f=new Fixture();await Assert.ThrowsAsync<ValidationException>(()=>f.Service.IssueAsync(f.Request,default));
        f.Reservation.Verify(x=>x.AcquireAsync(It.IsAny<CancellationToken>()),Times.Never);
    }
    [Theory][InlineData("Issued",true)][InlineData("Paid",true)][InlineData("Voided",false)]
    public async Task Pricing_cannot_rewrite_billed_periods_but_can_start_after_last_invoice(string status,bool blocked)
    {
        var f=new Fixture();f.Existing.Add(new(){Status=status,PeriodFrom=new(2026,10,1),PeriodTo=new(2026,10,31),Lines=[new(){ShiftId=f.Shift.Id}]});
        var plan=new ShiftPricingPlan("Monthly",175,25.5m,new(2026,10,15));
        if(blocked)await Assert.ThrowsAsync<ShiftConflictException>(()=>f.Settings.SaveAsync(f.Shift.Id,plan,default));
        else Assert.Contains((await f.Settings.SaveAsync(f.Shift.Id,plan,default)).Plans,p=>p==plan);
        var next=plan with { EffectiveFrom=new(2026,11,1) };
        Assert.Contains((await f.Settings.SaveAsync(f.Shift.Id,next,default)).Plans,p=>p==next);
    }
    [Fact]
    public async Task Updating_issuer_preserves_tenant_timezone_public_content_and_booking_settings()
    {
        var f=new Fixture();f.Profile.Slug="existing-tenant";f.Profile.LogoUrl="/logo.svg";f.Profile.MinHoursBeforeBooking=8;
        f.Profile.Pages["home"]=new(){Values=new(){["fi"]="Existing home content"}};
        var settings=new InvoiceIssuerSettings(" Updated issuer ","7654321-0",new(){Street="New Street",PostalCode="00100",City="Helsinki"},"new@example.test","123","FI21 1234 5600 0007 85","ndeafihh",30,25.5m,true);
        var result=await f.Settings.SaveIssuerAsync(settings,default);
        Assert.Equal("Updated issuer",result.CompanyName);Assert.True(result.IsSampleData);Assert.Equal("FI2112345600000785",result.BankAccountIBAN);Assert.Equal("NDEAFIHH",result.BankBic);
        Assert.Equal("Europe/Helsinki",f.Profile.TimeZoneId);Assert.Equal("existing-tenant",f.Profile.Slug);Assert.Equal("/logo.svg",f.Profile.LogoUrl);Assert.Equal(8,f.Profile.MinHoursBeforeBooking);Assert.Equal("Existing home content",f.Profile.Pages["home"].Values["fi"]);
    }
    // Friday visits in October 2026 at 16:00 Helsinki. Days without a completed form are unstarted.
    private static DateTime FridayStartUtc(int day) => new(2026, 10, day, day > 25 ? 14 : 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Unstarted_hourly_visits_are_excluded_until_ticked_in()
    {
        var f = new Fixture(completedDays: [2, 9, 16]);
        f.Pricing.Plans = [new("Hourly", 50, 25.5m, new(2026, 10, 1))];
        var preview = await f.Service.PreviewAsync(f.Request, default);
        var line = Assert.Single(preview.Invoice.Lines);
        Assert.Equal(3, line.Occurrences); Assert.Equal(6, line.Hours); Assert.Equal(300, line.NetAmount);
        Assert.Equal(new[] { FridayStartUtc(23), FridayStartUtc(30) }, preview.UnstartedVisits!.Select(u => u.StartUtc).ToArray());
        Assert.All(preview.UnstartedVisits!, u => Assert.False(u.Included));
    }

    [Fact]
    public async Task Ticking_an_unstarted_hourly_visit_bills_its_scheduled_hours()
    {
        var f = new Fixture(completedDays: [2, 9, 16]);
        f.Pricing.Plans = [new("Hourly", 50, 25.5m, new(2026, 10, 1))];
        var decisions = new[] { new UnstartedVisitDecision(f.Shift.Id, FridayStartUtc(23), true) };
        var preview = await f.Service.PreviewAsync(f.Request with { UnstartedDecisions = decisions }, default);
        var line = Assert.Single(preview.Invoice.Lines);
        Assert.Equal(4, line.Occurrences); Assert.Equal(8, line.Hours); Assert.Equal(400, line.NetAmount);
        Assert.Contains(preview.UnstartedVisits!, u => u.StartUtc == FridayStartUtc(23) && u.Included);
        Assert.Contains(preview.UnstartedVisits!, u => u.StartUtc == FridayStartUtc(30) && !u.Included);
    }

    [Fact]
    public async Task Unstarted_only_selection_still_previews_the_tick_list_and_cannot_be_issued_empty()
    {
        var f = new Fixture(completedDays: []);
        f.Pricing.Plans = [new("Hourly", 50, 25.5m, new(2026, 10, 1))];
        var preview = await f.Service.PreviewAsync(f.Request, default);
        Assert.Empty(preview.Invoice.Lines);
        Assert.Equal(5, preview.UnstartedVisits!.Count);
        Assert.All(preview.UnstartedVisits!, u => Assert.False(u.Included));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => f.Service.IssueAsync(f.Request with { PreviewToken = preview.PreviewToken }, default));
        f.Invoices.Verify(x => x.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
        // Ticking one visit gives the preview a billable line, so issuing becomes possible.
        var ticked = new[] { new UnstartedVisitDecision(f.Shift.Id, FridayStartUtc(23), true) };
        var included = await f.Service.PreviewAsync(f.Request with { UnstartedDecisions = ticked }, default);
        Assert.Equal(1, included.Invoice.Lines.Single().Occurrences);
    }

    [Fact]
    public async Task Ticking_an_unstarted_monthly_visit_counts_it_as_done_in_the_fraction()
    {
        var f = new Fixture(completedDays: [2, 9, 16]);
        var excluded = await f.Service.PreviewAsync(f.Request, default);
        Assert.Equal(90, excluded.Invoice.Lines.Single().NetAmount);
        var ticked = new[] { new UnstartedVisitDecision(f.Shift.Id, FridayStartUtc(23), true) };
        var included = await f.Service.PreviewAsync(f.Request with { UnstartedDecisions = ticked }, default);
        Assert.Equal(120, included.Invoice.Lines.Single().NetAmount);
        Assert.Contains("4/5 completed visits", included.Invoice.Lines.Single().Description);
    }

    [Fact]
    public async Task Changing_a_tick_changes_the_preview_token_so_issuing_a_stale_preview_is_refused()
    {
        var f = new Fixture(completedDays: [2, 9, 16]);
        f.Pricing.Plans = [new("Hourly", 50, 25.5m, new(2026, 10, 1))];
        var before = await f.Service.PreviewAsync(f.Request, default);
        var ticked = new[] { new UnstartedVisitDecision(f.Shift.Id, FridayStartUtc(23), true) };
        var after = await f.Service.PreviewAsync(f.Request with { UnstartedDecisions = ticked }, default);
        Assert.NotEqual(before.PreviewToken, after.PreviewToken);
        await Assert.ThrowsAsync<ShiftConflictException>(() => f.Service.IssueAsync(f.Request with { UnstartedDecisions = ticked, PreviewToken = before.PreviewToken }, default));
        f.Invoices.Verify(x => x.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_choice_for_a_visit_that_is_not_unstarted_is_refused_with_a_refresh_message()
    {
        var f = new Fixture(completedDays: [2, 9, 16]);
        f.Pricing.Plans = [new("Hourly", 50, 25.5m, new(2026, 10, 1))];
        var stale = new[] { new UnstartedVisitDecision(f.Shift.Id, FridayStartUtc(2), true) };
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Service.PreviewAsync(f.Request with { UnstartedDecisions = stale }, default));
        Assert.Equal("The unstarted visits changed. Refresh the preview and choose again.", Assert.Single(ex.Errors).ErrorMessage);
    }

    [Fact]
    public async Task The_same_unstarted_visit_cannot_be_chosen_twice()
    {
        var f = new Fixture(completedDays: [2, 9, 16]);
        var twice = new[] { new UnstartedVisitDecision(f.Shift.Id, FridayStartUtc(23), true), new UnstartedVisitDecision(f.Shift.Id, FridayStartUtc(23), false) };
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Service.PreviewAsync(f.Request with { UnstartedDecisions = twice }, default));
        Assert.Equal("Each unstarted visit can be chosen only once.", Assert.Single(ex.Errors).ErrorMessage);
    }

    [Fact]
    public async Task Partner_month_refuses_unstarted_visit_choices_with_a_clear_reason()
    {
        var f = new Fixture(completedDays: [2, 9, 16]);
        var partnerMonth = new ShiftInvoiceRequest(f.Company.Id, [], new(2026, 10, 1), new(2026, 10, 31), PartnerMonth: true,
            UnstartedDecisions: [new UnstartedVisitDecision(f.Shift.Id, FridayStartUtc(23), true)]);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Service.PreviewAsync(partnerMonth, default));
        Assert.Equal("Partner invoices do not use unstarted visit choices.", Assert.Single(ex.Errors).ErrorMessage);
    }

    private sealed class Fixture
    {
        public readonly Mock<IInvoiceRepository> Invoices=new();public readonly Mock<IShiftWorkReservation> Reservation=new();
        public readonly Company Company=new() { Id=Guid.NewGuid(),Name="Methehe Oy",BusinessId="1234567-8" };
        public readonly Shift Shift=new() { Id=Guid.NewGuid(),Name="Biweekly",IsActive=true,ValidFrom=new(2026,10,1),CreatedUtc=new(2026,10,1,0,0,0,DateTimeKind.Utc),Schedule=new(){Type=ShiftScheduleType.Weekly,WeeklyDay=DayOfWeek.Friday,WeeklyStart=TimeSpan.FromHours(16),WeeklyEnd=TimeSpan.FromHours(18)} };
        public readonly TenantProfile Profile=new() { CompanyName="ReadySet",BusinessId="1234567-8",TimeZoneId="Europe/Helsinki",CompanyAddress=new(){Street="Street 1",PostalCode="00100",City="Helsinki"},BankAccountIBAN="FI2112345600000785",BankBic="NDEAFIHH",PaymentTermsDays=14 };
        public readonly ShiftBillingProfile Pricing=new(){Plans=[new("Monthly",150,25.5m,new(2026,10,1))]};
        public readonly List<Shift> Selected=[];public readonly List<Invoice> Existing=[];public readonly ShiftInvoiceRequest Request;public readonly ShiftInvoiceService Service;public readonly ShiftBillingSettingsService Settings;
        public Fixture(DateTimeOffset? now = null, int[]? completedDays = null)
        {
            var branch=new Branch { Id=Guid.NewGuid(),CompanyId=Company.Id,Name="AAA1" };Shift.CompanyId=Company.Id;Shift.BranchId=branch.Id;Pricing.ShiftId=Shift.Id;Selected.Add(Shift);
            Request=new(Company.Id,[Shift.Id],new(2026,10,1),new(2026,10,31),new(){Street="Customer Street",PostalCode="00200",City="Helsinki"});
            var shifts=new Mock<IShiftRepository>();shifts.Setup(x=>x.GetAsync(Shift.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Shift);shifts.Setup(x=>x.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(),It.IsAny<CancellationToken>())).ReturnsAsync(()=>Selected);
            shifts.Setup(x=>x.ListOccurrenceExceptionsAsync(Shift.Id,It.IsAny<DateTime>(),It.IsAny<DateTime>(),It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftOccurrenceException>());
            shifts.Setup(x=>x.ListAssignmentHistoryAsync(Shift.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftAssignment>());
            shifts.Setup(x=>x.ListOccurrenceAssignmentsAsync(Shift.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftOccurrenceAssignment>());
            var billing=new Mock<IShiftBillingRepository>();billing.Setup(x=>x.GetAsync(Shift.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Pricing);
            var companies=new Mock<ICompanyRepository>();companies.Setup(x=>x.GetAsync(Company.Id,It.IsAny<CancellationToken>())).ReturnsAsync(Company);
            var branches=new Mock<IBranchRepository>();branches.Setup(x=>x.GetAsync(branch.Id,It.IsAny<CancellationToken>())).ReturnsAsync(branch);
            var forms=new Mock<IQualityCycleRepository>();// 16:00 Helsinki is 13:00 UTC until the 25 Oct change to standard time, and 14:00 UTC after it.
var completed=(completedDays ?? [2,9,16,23,30]).Select(d=>new QualityCycleForm{ShiftId=Shift.Id,EmployeeId=Guid.NewGuid(),IsSubmitted=true,ShiftOccurrenceUtc=new DateTime(2026,10,d,d>25?14:13,0,0,DateTimeKind.Utc)}).ToArray();forms.Setup(x=>x.ListFormsAsync(Shift.Id,It.IsAny<DateTime>(),It.IsAny<DateTime>(),It.IsAny<CancellationToken>())).ReturnsAsync(completed);
            var profiles=new Mock<ITenantProfileRepository>();profiles.Setup(x=>x.GetAsync("test",It.IsAny<CancellationToken>())).ReturnsAsync(Profile);
            var tenant=new Mock<ITenantContext>();tenant.SetupGet(x=>x.TenantId).Returns("test");
            Invoices.Setup(x=>x.ListAsync(null,null,It.IsAny<CancellationToken>())).ReturnsAsync(()=>Existing);Invoices.Setup(x=>x.NextInvoiceNumberForYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(7L);
            Reservation.Setup(x=>x.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Lease());
            Settings=new(shifts.Object,billing.Object,Invoices.Object,profiles.Object,tenant.Object,Reservation.Object);
            Service=new(shifts.Object,billing.Object,Invoices.Object,companies.Object,branches.Object,forms.Object,profiles.Object,tenant.Object,Reservation.Object,new Clock(now));
        }
    }
    private sealed class Lease:IAsyncDisposable { public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
    private sealed class Clock(DateTimeOffset? now):TimeProvider { public override DateTimeOffset GetUtcNow()=>now ?? new(2026,11,1,9,0,0,TimeSpan.Zero); }
}


