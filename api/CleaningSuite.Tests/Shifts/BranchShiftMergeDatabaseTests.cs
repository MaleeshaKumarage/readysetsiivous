using CleaningSuite.Application.Common;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using FluentValidation;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Fixture = CleaningSuite.Tests.Partners.BranchMainShiftDatabaseTests.Fixture;

namespace CleaningSuite.Tests.Shifts;

/// <summary>
/// The startup merge folds partner bookings that still own a shift into their property's main shift. It must never
/// lose work records, change what was invoiced, or let an invoiced visit be billed again. Real PostgreSQL throughout.
/// </summary>
[Collection("Database integration")]
public class BranchShiftMergeDatabaseTests
{
    private static BranchShiftMergeRunner Runner(Fixture f)
    {
        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new TenantRegistration { Slug = f.Tenant, Status = TenantRegistration.StatusActive }]);
        return new(f.Store, registry.Object, Fixture.Config(), NullLogger<BranchShiftMergeRunner>.Instance);
    }

    /// <summary>A booking in the shape used before main shifts: its own shift, visit, whole-shift assignment and price copy.</summary>
    private static async Task<(PartnerCleaningRequest Request, Shift Shift)> Legacy(Fixture f, string time, Employee cleaner, string status = "Accepted",
        string? notes = null, Branch? property = null)
    {
        var branch = property ?? f.Branch;
        PartnerRequestDto created;
        using (var session = f.Session()) created = await f.Service(session).Create(f.Partner.Id, new(Guid.NewGuid(), branch.Id, Fixture.Day, Fixture.Day, time, notes), default);
        var start = Fixture.DayStart.Add(TimeSpan.Parse(time)); var end = start.AddMinutes(120);
        var day = Fixture.Day.ToDateTime(TimeOnly.MinValue);
        var shift = Shift.Create(f.Company.Id, branch.Id, $"Partner visit {time}", new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, notes, day, day);
        shift.PartnerRequestId = created.Id; shift.RequiredPhotoLabels = ["Before", "After"]; shift.PartnerAccessInstructions = "Key box 1234";
        using var writer = f.Store.LightweightSession(f.Tenant);
        var request = (await writer.LoadAsync<PartnerCleaningRequest>(created.Id))!;
        request.LinkedShiftId = shift.Id; request.Status = status;
        if (status == "Accepted")
        {
            request.AcceptedEmployeeId = cleaner.Id; request.PlannedStartUtc = start; request.PlannedEndUtc = end; request.FirstAcceptedAtUtc = DateTime.UtcNow;
            var visit = ShiftOccurrenceException.Create(shift.Id, null, start, end, ShiftOccurrenceExceptionStatus.Extra, "Accepted partner cleaning", cleaner.Id);
            visit.Id = BranchShiftMerge.VisitId(request.Id);
            var assignment = ShiftAssignment.Create(shift.Id, cleaner.Id, "Accepted partner cleaning");
            assignment.AssignedAtUtc = start; assignment.Periods = [new() { FromUtc = start }];
            writer.Store(visit); writer.Store(assignment);
            writer.Store(new ShiftBillingProfile { Id = Guid.NewGuid(), ShiftId = shift.Id, Plans = [new("PerOccurrence", request.PriceNet, request.VatRatePercent, Fixture.Day)] });
        }
        else shift.Deactivate();
        writer.Store(request); writer.Store(shift);
        await writer.SaveChangesAsync();
        return (request, shift);
    }

    private static async Task Worked(Fixture f, Guid shiftId, Employee cleaner, DateTime start, bool submitted = true)
    {
        using var writer = f.Store.LightweightSession(f.Tenant);
        writer.Store(new QualityCycleForm { Id = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"), ShiftId = shiftId, CompanyId = f.Company.Id, BranchId = f.Branch.Id,
            EmployeeId = cleaner.Id, ShiftName = "Partner visit", ShiftOccurrenceUtc = start, ShiftOccurrenceEndUtc = start.AddMinutes(120), StartedAtUtc = start,
            IsSubmitted = submitted, EndedAtUtc = submitted ? start.AddMinutes(110) : null, SubmittedUtc = submitted ? start.AddMinutes(110) : null });
        await writer.SaveChangesAsync();
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Report_mode_describes_the_plan_and_changes_nothing()
    {
        using var f = await Fixture.Create();
        var first = await Legacy(f, "08:00", f.Cleaner);
        await Legacy(f, "12:00", f.Cleaner, "Cancelled");

        var result = Assert.Single(await Runner(f).RunAsync("Report", default));

        Assert.False(result.Applied); Assert.Equal(0, result.Merged); Assert.Equal(0, result.Removed);
        using var reader = f.Reader();
        Assert.Equal(2, (await reader.Query<Shift>().ToListAsync()).Count);
        Assert.DoesNotContain(await reader.Query<Shift>().ToListAsync(), x => x.IsBranchMain);
        Assert.Empty(await reader.Query<BranchShiftMergeRecord>().ToListAsync());
        Assert.Equal(first.Shift.Id, (await reader.LoadAsync<PartnerCleaningRequest>(first.Request.Id))!.LinkedShiftId);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Accepted_bookings_of_one_property_become_numbered_visits_on_one_main_shift()
    {
        using var f = await Fixture.Create();
        // Seeded out of visit order: numbers follow the visits' dates, not the order the rows were stored in.
        var later = await Legacy(f, "12:00", f.Second, notes: "Water the plants");
        var earlier = await Legacy(f, "08:00", f.Cleaner, notes: "Change the linen");

        var result = await Runner(f).RunTenantAsync(f.Tenant, apply: true, default);

        Assert.Equal(new BranchShiftMergeRunner.Result(f.Tenant, 2, 0, 0, 0, true), result);
        using (var reader = f.Reader())
        {
            var main = Assert.Single(await reader.Query<Shift>().ToListAsync());
            Assert.True(main.IsBranchMain); Assert.True(main.IsActive); Assert.Equal(Shift.BranchMainId(f.Branch.Id), main.Id);
            Assert.Equal(2, main.OccurrenceCounter); Assert.Equal("Key box 1234", main.PartnerAccessInstructions); Assert.Null(main.ValidUntil);
            var visits = (await reader.Query<ShiftOccurrenceException>().ToListAsync()).OrderBy(x => x.Sequence).ToArray();
            Assert.Equal(new[] { earlier.Request.Id, later.Request.Id }, visits.Select(x => x.PartnerRequestId!.Value).ToArray());
            Assert.Equal(new[] { earlier.Shift.Id, later.Shift.Id }, visits.Select(x => x.LegacyShiftId!.Value).ToArray());
            Assert.Equal(new[] { f.Cleaner.Id, f.Second.Id }, visits.Select(x => x.EmployeeId!.Value).ToArray());
            Assert.Equal(new[] { "Change the linen", "Water the plants" }, visits.Select(x => x.Notes).ToArray());
            Assert.All(visits, x => { Assert.Equal(main.Id, x.ShiftId); Assert.Equal(new[] { "Before", "After" }, x.RequiredPhotoLabels); Assert.Equal("PartnerPortal", x.Source); });
            // The old whole-shift assignments and price copies are gone; the price stays frozen on each request.
            Assert.Empty(await reader.Query<ShiftAssignment>().ToListAsync());
            Assert.Empty(await reader.Query<ShiftBillingProfile>().ToListAsync());
            var request = (await reader.LoadAsync<PartnerCleaningRequest>(earlier.Request.Id))!;
            Assert.Equal(main.Id, request.LinkedShiftId); Assert.Equal(visits[0].StartUtc, request.OccurrenceStartUtc); Assert.Equal(1, request.OccurrenceSequence);
            Assert.Equal("Accepted", request.Status); Assert.Equal(100m, request.PriceNet);
            // Everything that was changed or deleted is kept in a backup record under the old shift's id.
            var backup = (await reader.LoadAsync<BranchShiftMergeRecord>(earlier.Shift.Id))!;
            Assert.Equal(BranchShiftMergeRecord.OutcomeMerged, backup.Outcome); Assert.Equal(main.Id, backup.MainShiftId); Assert.Equal(1, backup.VisitNumber);
            Assert.Contains("Partner visit 08:00", backup.BackupJson); Assert.Contains(earlier.Shift.Id.ToString(), backup.BackupJson);
        }
        // Each cleaner still has exactly their own visit.
        using (var read = f.Session())
        {
            var handler = new QualityCycleHandlers.GetMyShiftsQueryHandler(new ShiftRepository(read), new QualityCycleRepository(read, f.Store),
                new EmployeeRepository(read), new ShiftWorkReservation(Fixture.Config(), read), f.Profiles.Object, Fixture.Context(f.Tenant));
            var mine = Assert.Single(await handler.Handle(new(f.Cleaner.Id, Fixture.DayStart, Fixture.DayStart.AddDays(1)), default));
            Assert.Equal(1, mine.VisitNumber); Assert.Equal("Change the linen", mine.Notes);
            Assert.Equal(2, Assert.Single(await handler.Handle(new(f.Second.Id, Fixture.DayStart, Fixture.DayStart.AddDays(1)), default)).VisitNumber);
        }
        // A second start finds nothing left to do, and a new booking continues the numbering.
        Assert.Equal(new BranchShiftMergeRunner.Result(f.Tenant, 0, 0, 0, 0, true), await Runner(f).RunTenantAsync(f.Tenant, apply: true, default));
        var next = await f.Accepted("16:00", f.Cleaner);
        using var after = f.Reader();
        Assert.Equal(3, (await after.LoadAsync<PartnerCleaningRequest>(next.Id))!.OccurrenceSequence);
        Assert.Single(await after.Query<Shift>().ToListAsync());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Work_records_move_with_their_visit_and_an_invoiced_visit_is_never_billed_again()
    {
        using var f = await Fixture.Create();
        var invoiced = await Legacy(f, "08:00", f.Cleaner);
        var unbilled = await Legacy(f, "12:00", f.Cleaner);
        await Worked(f, invoiced.Shift.Id, f.Cleaner, invoiced.Request.PlannedStartUtc!.Value);
        await Worked(f, unbilled.Shift.Id, f.Cleaner, unbilled.Request.PlannedStartUtc!.Value);
        using (var writer = f.Store.LightweightSession(f.Tenant))
        {
            // The first visit was invoiced for November before the merge, under its own shift's id.
            writer.Store(new Invoice { Id = Guid.NewGuid(), CompanyId = f.Company.Id, Status = Invoice.StatusIssued, InvoiceNumber = "2026-0001",
                PeriodFrom = new(2026, 11, 1), PeriodTo = new(2026, 11, 30), Total = new() { Net = 100, Vat = 25.5m },
                Lines = [new() { ShiftId = invoiced.Shift.Id, ShiftName = "Cleaning", Quantity = 1, UnitPriceNet = 100, NetAmount = 100, VatRatePercent = 25.5m, VatAmount = 25.5m }] });
            await writer.SaveChangesAsync();
        }

        var result = await Runner(f).RunTenantAsync(f.Tenant, apply: true, default);

        Assert.Equal(2, result.Merged); Assert.Equal(0, result.Failed);
        Guid mainId;
        using (var reader = f.Reader())
        {
            mainId = Assert.Single(await reader.Query<Shift>().ToListAsync()).Id;
            var forms = await reader.Query<QualityCycleForm>().ToListAsync();
            Assert.Equal(2, forms.Count);
            Assert.All(forms, form => { Assert.Equal(mainId, form.ShiftId); Assert.True(form.IsSubmitted); Assert.NotNull(form.EndedAtUtc); });
            Assert.Equal(new[] { invoiced.Shift.Id, unbilled.Shift.Id }.Order(), forms.Select(x => x.OriginalShiftId!.Value).Order());
            // The issued invoice is a snapshot and is not rewritten.
            Assert.Equal(invoiced.Shift.Id, Assert.Single(Assert.Single(await reader.Query<Invoice>().ToListAsync()).Lines).ShiftId);
        }
        using (var session = f.Session())
        {
            // The partner still sees both visits as completed.
            Assert.Equal("Completed", (await f.Service(session).Get(f.Partner.Id, invoiced.Request.Id, default)).Status);
            Assert.Equal("Completed", (await f.Service(session).Get(f.Partner.Id, unbilled.Request.Id, default)).Status);
        }
        var month = new ShiftInvoiceRequest(f.Company.Id, [], new(2026, 11, 1), new(2026, 11, 30),
            new Address { Street = "Customer 1", PostalCode = "00100", City = "Helsinki" }, PartnerMonth: true);
        using (var session = f.Session())
        {
            var invoices = f.Invoices(session);
            var preview = await invoices.PreviewAsync(month, default);
            // Only the visit that was never invoiced is offered, at the price frozen on its booking, with its visit number.
            var line = Assert.Single(preview.Invoice.Lines);
            Assert.Equal(new[] { unbilled.Request.PlannedStartUtc!.Value }, line.OccurrenceStartsUtc);
            Assert.Equal(100m, line.NetAmount); Assert.Equal(25.5m, line.VatRatePercent); Assert.Equal(new[] { 2 }, line.VisitNumbers);
            await invoices.IssueAsync(month with { PreviewToken = preview.PreviewToken }, default);
        }
        using (var session = f.Session())
            await Assert.ThrowsAsync<ValidationException>(() => f.Invoices(session).PreviewAsync(month, default));
        using (var session = f.Session())
        {
            // The visit invoiced before the merge is still locked against cancelling.
            var stored = await f.Service(session).Get(f.Partner.Id, invoiced.Request.Id, default);
            await Assert.ThrowsAsync<ShiftConflictException>(() => f.Service(session).Cancel(f.Admin.Id, invoiced.Request.Id, stored.Version, true, "Late", default));
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_cancelled_bookings_leftover_shift_is_removed_and_its_request_kept()
    {
        using var f = await Fixture.Create();
        var cancelled = await Legacy(f, "08:00", f.Cleaner, "Cancelled");
        var reopened = await Legacy(f, "12:00", f.Cleaner, "Requested");

        var result = await Runner(f).RunTenantAsync(f.Tenant, apply: true, default);

        Assert.Equal(new BranchShiftMergeRunner.Result(f.Tenant, 0, 2, 0, 0, true), result);
        using var reader = f.Reader();
        Assert.Empty(await reader.Query<Shift>().ToListAsync());
        var request = (await reader.LoadAsync<PartnerCleaningRequest>(cancelled.Request.Id))!;
        Assert.Equal("Cancelled", request.Status); Assert.Null(request.LinkedShiftId);
        Assert.Equal("Requested", (await reader.LoadAsync<PartnerCleaningRequest>(reopened.Request.Id))!.Status);
        Assert.Equal(BranchShiftMergeRecord.OutcomeRemoved, (await reader.LoadAsync<BranchShiftMergeRecord>(cancelled.Shift.Id))!.Outcome);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Anything_unusual_is_kept_untouched_for_a_person_to_review()
    {
        using var f = await Fixture.Create();
        // Work recorded at another time than the booking's visit: which visit it belongs to is not certain.
        var moved = await Legacy(f, "08:00", f.Cleaner);
        await Worked(f, moved.Shift.Id, f.Cleaner, moved.Request.PlannedStartUtc!.Value.AddHours(-3));
        // Two bookings at exactly the same time: work records find a visit by its start, so only the first is merged.
        var twinA = await Legacy(f, "12:00", f.Cleaner);
        var twinB = await Legacy(f, "12:00", f.Second);
        // A cancelled booking that nevertheless has work records.
        var odd = await Legacy(f, "15:00", f.Cleaner, "Cancelled");
        await Worked(f, odd.Shift.Id, f.Cleaner, Fixture.DayStart.AddHours(15), submitted: false);
        // A property without size and cleaning type cannot name a main shift yet.
        var bare = Branch.Create(f.Company.Id, "Bare property"); bare.SetPricing(80, 25.5m, 60, effectiveFrom: new DateOnly(2026, 1, 1)); bare.SetPropertyDetails(20, "Normal");
        using (var writer = f.Store.LightweightSession(f.Tenant)) { writer.Store(bare); await writer.SaveChangesAsync(); }
        var unnamed = await Legacy(f, "17:00", f.Second, property: bare);
        using (var writer = f.Store.LightweightSession(f.Tenant))
        {
            var stored = await writer.LoadAsync<Branch>(bare.Id); stored!.SizeM2 = null; writer.Store(stored); await writer.SaveChangesAsync();
        }

        var result = await Runner(f).RunTenantAsync(f.Tenant, apply: true, default);

        Assert.Equal(1, result.Merged); Assert.Equal(0, result.Removed); Assert.Equal(4, result.Kept); Assert.Equal(0, result.Failed);
        using var reader = f.Reader();
        var shifts = await reader.Query<Shift>().ToListAsync();
        var kept = shifts.Where(x => !x.IsBranchMain).Select(x => x.Id).Order().ToArray();
        var merged = new[] { twinA.Shift.Id, twinB.Shift.Id }.Single(id => !kept.Contains(id));
        Assert.Equal(new[] { moved.Shift.Id, odd.Shift.Id, unnamed.Shift.Id }.Append(merged == twinA.Shift.Id ? twinB.Shift.Id : twinA.Shift.Id).Order(), kept);
        // A kept shift is exactly as it was: still linked, still assigned, its work records still under it.
        Assert.Equal(moved.Shift.Id, (await reader.LoadAsync<PartnerCleaningRequest>(moved.Request.Id))!.LinkedShiftId);
        Assert.Contains(await reader.Query<ShiftAssignment>().ToListAsync(), x => x.ShiftId == moved.Shift.Id && x.IsActive);
        Assert.Contains(await reader.Query<QualityCycleForm>().ToListAsync(), x => x.ShiftId == moved.Shift.Id && x.OriginalShiftId is null);
        Assert.Null(await reader.LoadAsync<BranchShiftMergeRecord>(moved.Shift.Id));
        Assert.Single(shifts, x => x.IsBranchMain);
    }

    /// <summary>A one-time shift as staff created them before Add visit: its own shift, whole-shift cleaners and price.</summary>
    private static async Task<Shift> OneTime(Fixture f, string start, string end, Employee[] cleaners, ShiftBillingProfile? billing = null,
        bool active = true, string? notes = null, Guid? template = null)
    {
        var day = Fixture.Day.ToDateTime(TimeOnly.MinValue);
        var shift = Shift.Create(f.Company.Id, f.Branch.Id, $"One-time {start}", new ShiftSchedule { Type = ShiftScheduleType.OneTime, OneTimeDate = day,
            OneTimeStart = TimeSpan.Parse(start), OneTimeEnd = TimeSpan.Parse(end) }, notes, day, day, template);
        if (!active) shift.Deactivate();
        using var writer = f.Store.LightweightSession(f.Tenant);
        writer.Store(shift);
        foreach (var cleaner in cleaners) writer.Store(ShiftAssignment.Create(shift.Id, cleaner.Id, "Assigned"));
        if (billing is not null) { billing.Id = Guid.NewGuid(); billing.ShiftId = shift.Id; writer.Store(billing); }
        await writer.SaveChangesAsync();
        return shift;
    }

    private static async Task<IReadOnlyList<MyShiftOccurrenceDto>> Mine(Fixture f, Employee cleaner)
    {
        using var read = f.Session();
        var handler = new QualityCycleHandlers.GetMyShiftsQueryHandler(new ShiftRepository(read), new QualityCycleRepository(read, f.Store),
            new EmployeeRepository(read), new ShiftWorkReservation(Fixture.Config(), read), f.Profiles.Object, Fixture.Context(f.Tenant));
        return await handler.Handle(new(cleaner.Id, Fixture.DayStart, Fixture.DayStart.AddDays(1)), default);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task One_time_shifts_become_numbered_visits_held_by_the_same_cleaners()
    {
        using var f = await Fixture.Create();
        var solo = await OneTime(f, "08:00", "10:00", [f.Cleaner], new ShiftBillingProfile { BranchPriceEffectiveFrom = f.Branch.Versions[0].EffectiveFrom }, notes: "Bring the ladder");
        var team = await OneTime(f, "12:00", "14:00", [f.Cleaner, f.Second]);
        var nobody = await OneTime(f, "16:00", "17:00", []);
        Assert.Equal(2, (await Mine(f, f.Cleaner)).Count);

        var result = await Runner(f).RunTenantAsync(f.Tenant, apply: true, default);

        Assert.Equal(new BranchShiftMergeRunner.Result(f.Tenant, 3, 0, 0, 0, true), result);
        using (var reader = f.Reader())
        {
            var main = Assert.Single(await reader.Query<Shift>().ToListAsync());
            Assert.True(main.IsBranchMain); Assert.Equal(3, main.OccurrenceCounter);
            var visits = (await reader.Query<ShiftOccurrenceException>().ToListAsync()).OrderBy(x => x.Sequence).ToArray();
            Assert.Equal(new[] { solo.Id, team.Id, nobody.Id }, visits.Select(x => x.LegacyShiftId!.Value).ToArray());
            Assert.All(visits, x => { Assert.Equal("AdminPanel", x.Source); Assert.Equal(ShiftOccurrenceExceptionStatus.Extra, x.Status); Assert.Null(x.OfferedEmployeeId); });
            Assert.Equal(Fixture.DayStart.AddHours(8), visits[0].StartUtc); Assert.Equal(Fixture.DayStart.AddHours(10), visits[0].EndUtc);
            Assert.Equal(f.Cleaner.Id, visits[0].EmployeeId); Assert.Equal("Bring the ladder", visits[0].Notes);
            // A team keeps every member through a roster record for that visit.
            Assert.Null(visits[1].EmployeeId);
            var roster = Assert.Single(await reader.Query<ShiftOccurrenceAssignment>().ToListAsync());
            Assert.Equal(visits[1].Id, roster.ExtraExceptionId); Assert.Equal(new[] { f.Cleaner.Id, f.Second.Id }.Order(), roster.EmployeeIds.Order());
            Assert.Null(visits[2].EmployeeId);
            Assert.Empty(await reader.Query<ShiftAssignment>().ToListAsync());
            // The property price already applies, so no separate visit price is kept.
            Assert.Empty(await reader.Query<ShiftBillingProfile>().ToListAsync());
            Assert.Equal(BranchShiftMergeRecord.OutcomeMerged, (await reader.LoadAsync<BranchShiftMergeRecord>(solo.Id))!.Outcome);
        }
        // The cleaners see exactly the visits they had before.
        var mine = await Mine(f, f.Cleaner);
        Assert.Equal(new int?[] { 1, 2 }, mine.Select(x => x.VisitNumber).ToArray());
        Assert.Equal(2, Assert.Single(await Mine(f, f.Second)).VisitNumber);
        Assert.Equal(new BranchShiftMergeRunner.Result(f.Tenant, 0, 0, 0, 0, true), await Runner(f).RunTenantAsync(f.Tenant, apply: true, default));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_one_time_shifts_own_price_is_kept_and_an_invoiced_one_is_not_billed_again()
    {
        using var f = await Fixture.Create();
        // The property charges 100 EUR at 25.5 %. This visit was agreed at 55 EUR at 14 %.
        var special = await OneTime(f, "08:00", "10:00", [f.Cleaner], new ShiftBillingProfile { Plans = [new("PerOccurrence", 55, 14, new DateOnly(2026, 1, 1))] });
        var invoiced = await OneTime(f, "12:00", "14:00", [f.Cleaner], new ShiftBillingProfile { BranchPriceEffectiveFrom = f.Branch.Versions[0].EffectiveFrom });
        await Worked(f, special.Id, f.Cleaner, Fixture.DayStart.AddHours(8));
        await Worked(f, invoiced.Id, f.Cleaner, Fixture.DayStart.AddHours(12));
        using (var writer = f.Store.LightweightSession(f.Tenant))
        {
            writer.Store(new Invoice { Id = Guid.NewGuid(), CompanyId = f.Company.Id, Status = Invoice.StatusPaid, InvoiceNumber = "2026-0001",
                PeriodFrom = new(2026, 11, 1), PeriodTo = new(2026, 11, 30), Total = new() { Net = 100, Vat = 25.5m },
                Lines = [new() { ShiftId = invoiced.Id, ShiftName = "One-time 12:00", Quantity = 1, UnitPriceNet = 100, NetAmount = 100, VatRatePercent = 25.5m, VatAmount = 25.5m }] });
            await writer.SaveChangesAsync();
        }

        var result = await Runner(f).RunTenantAsync(f.Tenant, apply: true, default);

        Assert.Equal(2, result.Merged); Assert.Equal(0, result.Kept);
        Guid mainId;
        using (var reader = f.Reader())
        {
            mainId = Assert.Single(await reader.Query<Shift>().ToListAsync()).Id;
            var kept = Assert.Single(Assert.Single(await reader.Query<ShiftBillingProfile>().ToListAsync()).Overrides);
            Assert.Equal(55m, kept.RateNet); Assert.Equal(14m, kept.VatRatePercent); Assert.Equal(Fixture.DayStart.AddHours(8), kept.OccurrenceStartUtc);
            Assert.All(await reader.Query<QualityCycleForm>().ToListAsync(), form => Assert.Equal(mainId, form.ShiftId));
        }
        var request = new ShiftInvoiceRequest(f.Company.Id, [mainId], new(2026, 11, 1), new(2026, 11, 30),
            new Address { Street = "Customer 1", PostalCode = "00100", City = "Helsinki" });
        using var session = f.Session();
        var line = Assert.Single((await f.Invoices(session).PreviewAsync(request, default)).Invoice.Lines);
        // Only the visit that was never invoiced, at its own agreed price and VAT.
        Assert.Equal(55m, line.NetAmount); Assert.Equal(14m, line.VatRatePercent); Assert.Equal(new[] { 1 }, line.VisitNumbers);
        Assert.Equal(BranchShiftMerge.KeptPriceReason, line.OverrideReason);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_deactivated_one_time_shift_is_removed_and_unusual_ones_are_kept()
    {
        using var f = await Fixture.Create();
        var dropped = await OneTime(f, "06:00", "07:00", [f.Cleaner], active: false);
        // Priced by the hour, unlike the property: the main shift cannot charge it the same way.
        var hourly = await OneTime(f, "08:00", "10:00", [f.Cleaner], new ShiftBillingProfile { Plans = [new("Hourly", 30, 25.5m, new DateOnly(2026, 1, 1))] });
        // Its visit was moved by hand.
        var moved = await OneTime(f, "12:00", "14:00", [f.Cleaner]);
        // Deactivated, but work was recorded.
        var worked = await OneTime(f, "16:00", "17:00", [f.Cleaner], active: false);
        await Worked(f, worked.Id, f.Cleaner, Fixture.DayStart.AddHours(16));
        using (var writer = f.Store.LightweightSession(f.Tenant))
        {
            writer.Store(ShiftOccurrenceException.Create(moved.Id, Fixture.DayStart.AddHours(12), Fixture.DayStart.AddHours(13), Fixture.DayStart.AddHours(15),
                ShiftOccurrenceExceptionStatus.Moved, "Customer asked"));
            await writer.SaveChangesAsync();
        }

        var result = await Runner(f).RunTenantAsync(f.Tenant, apply: true, default);

        Assert.Equal(new BranchShiftMergeRunner.Result(f.Tenant, 0, 1, 3, 0, true), result);
        using var reader = f.Reader();
        Assert.Equal(new[] { hourly.Id, moved.Id, worked.Id }.Order(), (await reader.Query<Shift>().ToListAsync()).Select(x => x.Id).Order());
        Assert.Equal(BranchShiftMergeRecord.OutcomeRemoved, (await reader.LoadAsync<BranchShiftMergeRecord>(dropped.Id))!.Outcome);
        // Kept shifts keep their cleaners, price and work records exactly as they were.
        Assert.Equal(3, (await reader.Query<ShiftAssignment>().ToListAsync()).Count);
        Assert.Equal("Hourly", Assert.Single(Assert.Single(await reader.Query<ShiftBillingProfile>().ToListAsync()).Plans).Mode);
        Assert.Equal(worked.Id, Assert.Single(await reader.Query<QualityCycleForm>().ToListAsync()).ShiftId);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task An_unfinished_one_time_visit_with_another_checklist_than_the_main_shift_is_kept()
    {
        using var f = await Fixture.Create();
        var mainTemplate = QualityCycleTemplate.Create("Property checklist", ["Kitchen"], companyId: f.Company.Id);
        var otherTemplate = QualityCycleTemplate.Create("Deep clean checklist", ["Oven"], companyId: f.Company.Id);
        f.Company.IsQualityCycleEnabled = true; f.Company.QualityCycleTemplateId = mainTemplate.Id;
        using (var writer = f.Store.LightweightSession(f.Tenant)) { writer.Store(mainTemplate, otherTemplate); writer.Store(f.Company); await writer.SaveChangesAsync(); }
        await f.Accepted("06:00", f.Second);
        var pending = await OneTime(f, "12:00", "14:00", [f.Cleaner], template: otherTemplate.Id);
        var finished = await OneTime(f, "16:00", "17:00", [f.Cleaner], template: otherTemplate.Id);
        await Worked(f, finished.Id, f.Cleaner, Fixture.DayStart.AddHours(16));

        var result = await Runner(f).RunTenantAsync(f.Tenant, apply: true, default);

        // The finished visit already has its checklist recorded, so it merges; the pending one would change checklist.
        Assert.Equal(1, result.Merged); Assert.Equal(1, result.Kept);
        using var reader = f.Reader();
        Assert.Equal(pending.Id, Assert.Single(await reader.Query<Shift>().ToListAsync(), x => !x.IsBranchMain).Id);
        Assert.Equal(mainTemplate.Id, (await reader.Query<Shift>().ToListAsync()).Single(x => x.IsBranchMain).QualityCycleTemplateId);
    }

    [Fact]
    public void The_plan_only_looks_at_bookings_that_own_a_shift()
    {
        var company = Guid.NewGuid(); var branch = Guid.NewGuid();
        var recurring = Shift.Create(company, branch, "Weekly", new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, DateTime.Today, null);
        var main = Shift.Create(company, branch, "Main", new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, DateTime.Today, null);
        main.IsBranchMain = true;
        Assert.Empty(BranchShiftMerge.Plan([recurring, main], [], [], [], [], [], []));
    }
}
