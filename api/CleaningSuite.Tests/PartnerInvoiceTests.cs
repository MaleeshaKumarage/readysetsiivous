using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using FluentValidation;
using Moq;
namespace CleaningSuite.Tests.Invoicing;

public class PartnerInvoiceTests
{
    [Fact]
    public async Task Company_month_selects_more_than_fifty_completed_unbilled_visits_using_frozen_prices()
    {
        var f = new Fixture(60);
        f.Forms[f.Requests[0].LinkedShiftId!.Value].Clear();
        f.Requests[1].Status = "Cancelled";
        f.Existing.Add(new() { Status = Invoice.StatusIssued, PeriodFrom = new(2026,10,1), PeriodTo = new(2026,10,31), Lines = [new() { ShiftId = f.Requests[2].LinkedShiftId }] });
        f.Existing.Add(new() { Status = Invoice.StatusPaid, PeriodFrom = new(2026,10,1), PeriodTo = new(2026,10,31), Lines = [new() { ShiftId = f.Requests[3].LinkedShiftId }] });
        f.Existing.Add(new() { Status = Invoice.StatusVoided, PeriodFrom = new(2026,10,1), PeriodTo = new(2026,10,31), Lines = [new() { ShiftId = f.Requests[4].LinkedShiftId }] });
        f.Requests[5].PlannedStartUtc = new(2026,11,1,12,0,0,DateTimeKind.Utc);
        var preview = await f.Service.PreviewAsync(f.Input, default);
        Assert.Equal(55, preview.Invoice.Lines.Count);
        Assert.Equal(5500, preview.Invoice.Total.Net);
        Assert.All(preview.Invoice.Lines, l => { Assert.Equal(100, l.UnitPriceNet); Assert.Equal(25.5m, l.VatRatePercent); Assert.Equal(1, l.Occurrences); });
        f.Invoices.Verify(x => x.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task New_completed_visit_invalidates_month_preview_before_issue()
    {
        var f = new Fixture(2); var second = f.Forms[f.Requests[1].LinkedShiftId!.Value].Single(); second.IsSubmitted = false;
        var preview = await f.Service.PreviewAsync(f.Input, default); second.IsSubmitted = true;
        await Assert.ThrowsAsync<ShiftConflictException>(() => f.Service.IssueAsync(f.Input with { PreviewToken = preview.PreviewToken }, default));
        f.Invoices.Verify(x => x.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task Month_mode_requires_full_month_and_no_explicit_shifts()
    {
        var f = new Fixture(1);
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.PreviewAsync(f.Input with { To = new(2026,10,30) }, default));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.PreviewAsync(f.Input with { ShiftIds = [f.Requests[0].LinkedShiftId!.Value] }, default));
    }
    [Fact]
    public async Task Frozen_partner_billing_cannot_be_changed_through_ordinary_pricing_routes()
    {
        var f = new Fixture(1); var id = f.Requests[0].LinkedShiftId!.Value;
        await Assert.ThrowsAsync<ShiftConflictException>(() => f.Settings.SaveAsync(id, new("PerOccurrence",5,25.5m,new(2026,10,1)), default));
        await Assert.ThrowsAsync<ShiftConflictException>(() => f.Settings.SaveOccurrenceAsync(id,new(f.Requests[0].PlannedStartUtc!.Value,5,"override"),default));
    }
    private sealed class Fixture
    {
        public List<PartnerCleaningRequest> Requests = []; public Dictionary<Guid,List<QualityCycleForm>> Forms = []; public List<Invoice> Existing = [];
        public Mock<IInvoiceRepository> Invoices = new(); public ShiftInvoiceService Service; public ShiftBillingSettingsService Settings; public ShiftInvoiceRequest Input;
        public Fixture(int count)
        {
            var company = new Company { Id=Guid.NewGuid(), Name="Property owner", IsActive=true, IsPartnerEnabled=true };
            var branch = new Branch { Id=Guid.NewGuid(), CompanyId=company.Id, Name="Apartment" };
            var profile = new TenantProfile { TimeZoneId="Europe/Helsinki", PaymentTermsDays=14 };
            var shifts = new Mock<IShiftRepository>(); var selected = new Dictionary<Guid,Shift>();
            for(var i=0;i<count;i++)
            {
                var request = new PartnerCleaningRequest { Id=Guid.NewGuid(),CompanyId=company.Id,BranchId=branch.Id,PriceNet=100,VatRatePercent=25.5m,EstimatedDurationMinutes=120,Status="Accepted",FirstAcceptedAtUtc=DateTime.UtcNow,LinkedShiftId=Guid.NewGuid(),PlannedStartUtc=new(2026,10,15,12,0,0,DateTimeKind.Utc) };
                var shift = new Shift { Id=request.LinkedShiftId.Value,PartnerRequestId=request.Id,CompanyId=company.Id,BranchId=branch.Id,Name="Partner cleaning",IsActive=true,ValidFrom=new(2026,10,15),ValidUntil=new(2026,10,15),CreatedUtc=new(2026,10,1,0,0,0,DateTimeKind.Utc),Schedule=new(){Type=ShiftScheduleType.OnCallFlexible} };
                selected.Add(shift.Id,shift); Requests.Add(request); var employeeId=Guid.NewGuid();
                Forms.Add(shift.Id,[new(){Id=Guid.NewGuid(),ShiftId=shift.Id,EmployeeId=employeeId,ShiftOccurrenceUtc=request.PlannedStartUtc.Value,IsSubmitted=true}]);
                shifts.Setup(x=>x.GetAsync(shift.Id,It.IsAny<CancellationToken>())).ReturnsAsync(shift);
                shifts.Setup(x=>x.ListOccurrenceExceptionsAsync(shift.Id,It.IsAny<DateTime>(),It.IsAny<DateTime>(),It.IsAny<CancellationToken>())).ReturnsAsync([ShiftOccurrenceException.Create(shift.Id,null,request.PlannedStartUtc.Value,request.PlannedStartUtc.Value.AddHours(2),ShiftOccurrenceExceptionStatus.Extra,"Partner cleaning",employeeId)]);
                shifts.Setup(x=>x.ListAssignmentHistoryAsync(shift.Id,It.IsAny<CancellationToken>())).ReturnsAsync([]);
                shifts.Setup(x=>x.ListOccurrenceAssignmentsAsync(shift.Id,It.IsAny<CancellationToken>())).ReturnsAsync([]);
            }
            shifts.Setup(x=>x.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(),It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyCollection<Guid> ids,CancellationToken _) => (IReadOnlyList<Shift>)ids.Select(id=>selected[id]).ToArray());
            var billing=new Mock<IShiftBillingRepository>(); billing.Setup(x=>x.GetAsync(It.IsAny<Guid>(),It.IsAny<CancellationToken>())).ReturnsAsync(new ShiftBillingProfile {Plans=[new("PerOccurrence",999,0,new(2000,1,1))]});
            var companies=new Mock<ICompanyRepository>(); companies.Setup(x=>x.GetAsync(company.Id,It.IsAny<CancellationToken>())).ReturnsAsync(company);
            var branches=new Mock<IBranchRepository>(); branches.Setup(x=>x.GetAsync(branch.Id,It.IsAny<CancellationToken>())).ReturnsAsync(branch);
            var forms=new Mock<IQualityCycleRepository>(); forms.Setup(x=>x.ListFormsAsync(It.IsAny<Guid>(),It.IsAny<DateTime?>(),It.IsAny<DateTime?>(),It.IsAny<CancellationToken>())).ReturnsAsync((Guid id,DateTime? _,DateTime? __,CancellationToken ___)=>(IReadOnlyList<QualityCycleForm>)Forms[id]);
            var profiles=new Mock<ITenantProfileRepository>(); profiles.Setup(x=>x.GetAsync("test",It.IsAny<CancellationToken>())).ReturnsAsync(profile);
            var tenant=new Mock<ITenantContext>(); tenant.SetupGet(x=>x.TenantId).Returns("test");
            var reserve=new Mock<IShiftWorkReservation>(); reserve.Setup(x=>x.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Lease());
            Invoices.Setup(x=>x.ListAsync(null,null,It.IsAny<CancellationToken>())).ReturnsAsync(()=>Existing);
            var source=new Mock<IPartnerInvoiceSource>(); source.Setup(x=>x.ListCompanyAsync(company.Id,It.IsAny<CancellationToken>())).ReturnsAsync(()=>Requests);
            Service=new(shifts.Object,billing.Object,Invoices.Object,companies.Object,branches.Object,forms.Object,profiles.Object,tenant.Object,reserve.Object,TimeProvider.System,source.Object);
            Settings=new(shifts.Object,billing.Object,Invoices.Object,profiles.Object,tenant.Object,reserve.Object);
            Input=new(company.Id,[],new(2026,10,1),new(2026,10,31),PartnerMonth:true);
        }
    }
    private sealed class Lease:IAsyncDisposable { public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
}
