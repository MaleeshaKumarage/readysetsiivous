using CleaningSuite.Application.Common;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.Partners;
using FluentValidation;
using Marten;
using Fixture = CleaningSuite.Tests.Partners.BranchMainShiftDatabaseTests.Fixture;

namespace CleaningSuite.Tests.Shifts;

/// <summary>
/// Staff add visits to a branch's main shift by date or date range. Each visit gets the next number, the source
/// "AdminPanel", and an offer to the shift's default cleaner or to a chosen one. Real PostgreSQL throughout.
/// </summary>
[Collection("Database integration")]
public class BranchVisitServiceDatabaseTests
{
    private static AddBranchVisit OnDay(Fixture f, string start = "08:00", string end = "10:00", Guid? employee = null, bool useDefault = true, string? notes = null) =>
        new(f.Branch.Id, Fixture.Day, Fixture.Day, start, end, notes, employee, useDefault);

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task The_first_visit_creates_the_main_shift_and_numbers_continue_across_admin_and_partner_visits()
    {
        using var f = await Fixture.Create();
        BranchVisitDto first;
        using (var session = f.Session()) first = await f.Visits(session).Add(OnDay(f, notes: " Ring the back door bell "), default);

        Assert.Equal(1, first.Number); Assert.Equal(ShiftOccurrenceException.SourceAdminPanel, first.Source);
        Assert.Equal(BranchVisitService.StatusUnassigned, first.Status); Assert.Equal("Ring the back door bell", first.Notes);
        Assert.Equal(Shift.BranchMainId(f.Branch.Id), first.ShiftId);
        Assert.Equal(Fixture.Day, first.FromDate); Assert.Equal(Fixture.Day, first.ToDate);
        var booked = await f.Accepted("12:00", f.Cleaner);
        BranchVisitDto third;
        using (var session = f.Session()) third = await f.Visits(session).Add(OnDay(f, "15:00", "16:00"), default);
        Assert.Equal(3, third.Number);

        using var reader = f.Reader();
        var shift = Assert.Single(await reader.Query<Shift>().ToListAsync());
        Assert.True(shift.IsBranchMain); Assert.Equal(booked.LinkedShiftId, shift.Id); Assert.Equal(3, shift.OccurrenceCounter);
        var visits = (await reader.Query<ShiftOccurrenceException>().ToListAsync()).OrderBy(x => x.Sequence).ToArray();
        Assert.Equal(new[] { "AdminPanel", "PartnerPortal", "AdminPanel" }, visits.Select(x => x.Source).ToArray());
        using var read = f.Session();
        var main = await f.Visits(read).Get(f.Branch.Id, Fixture.DayStart, Fixture.DayStart.AddDays(1), default);
        Assert.Equal(new[] { 1, 2, 3 }, main!.Visits.Select(x => x.Number).ToArray());
        Assert.NotNull(main.Visits[1].PartnerRequestId);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_date_range_is_one_numbered_visit_covering_the_whole_range()
    {
        using var f = await Fixture.Create();
        BranchVisitDto visit;
        using (var session = f.Session())
            visit = await f.Visits(session).Add(new(f.Branch.Id, Fixture.Day, Fixture.Day.AddDays(4), "08:00", "16:00"), default);

        Assert.Equal(1, visit.Number); Assert.Equal(Fixture.Day, visit.FromDate); Assert.Equal(Fixture.Day.AddDays(4), visit.ToDate);
        Assert.Equal(Fixture.DayStart.AddHours(8), visit.StartUtc); Assert.Equal(Fixture.DayStart.AddDays(4).AddHours(16), visit.EndUtc);
        using var reader = f.Reader();
        Assert.Single(await reader.Query<ShiftOccurrenceException>().ToListAsync());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Without_a_default_cleaner_a_new_visit_is_nobodys_and_a_chosen_cleaner_overrides_the_default()
    {
        using var f = await Fixture.Create();
        BranchVisitDto unassigned, chosen, opted;
        using (var session = f.Session()) unassigned = await f.Visits(session).Add(OnDay(f), default);
        using (var session = f.Session()) await f.Visits(session).SetDefaultEmployee(unassigned.ShiftId, new(f.Cleaner.Id), default);
        using (var session = f.Session()) chosen = await f.Visits(session).Add(OnDay(f, "11:00", "12:00", f.Second.Id), default);
        using (var session = f.Session()) opted = await f.Visits(session).Add(OnDay(f, "13:00", "14:00", useDefault: false), default);

        Assert.Equal(BranchVisitService.StatusUnassigned, unassigned.Status);
        Assert.Equal(f.Second.Id, chosen.OfferedEmployeeId);
        Assert.Equal(BranchVisitService.StatusUnassigned, opted.Status);
        // Setting the default afterwards does not touch a visit that was already added.
        using var reader = f.Reader();
        Assert.Null((await reader.LoadAsync<ShiftOccurrenceException>(unassigned.Id))!.OfferedEmployeeId);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task The_default_cleaner_gets_an_offer_and_holds_the_visit_only_after_accepting()
    {
        using var f = await Fixture.Create();
        BranchVisitDto seed, offered;
        using (var session = f.Session()) seed = await f.Visits(session).Add(OnDay(f, "06:00", "07:00"), default);
        using (var session = f.Session()) await f.Visits(session).SetDefaultEmployee(seed.ShiftId, new(f.Cleaner.Id), default);
        using (var session = f.Session()) offered = await f.Visits(session).Add(OnDay(f, notes: "Windows too"), default);

        Assert.Equal(BranchVisitService.StatusOffered, offered.Status); Assert.Equal(f.Cleaner.Id, offered.OfferedEmployeeId); Assert.Null(offered.EmployeeId);
        Assert.Empty(await Mine(f, f.Cleaner));
        PartnerCleanerOfferDto offer;
        using (var session = f.Session())
        {
            offer = Assert.Single(await f.Visits(session).Offers(f.Cleaner.Id, default));
            Assert.Equal(offered.Id, offer.RequestId); Assert.Equal("Apartment", offer.BranchName); Assert.Equal("Windows too", offer.Notes);
            Assert.Equal(Fixture.Day, offer.FromDate); Assert.Equal("08:00", offer.ExactStartLocalTime); Assert.Equal(120, offer.EstimatedDurationMinutes);
            Assert.Empty(await f.Visits(session).Offers(f.Second.Id, default));
        }
        // Another cleaner cannot take the offer, and an answer to an older version is refused.
        using (var session = f.Session()) Assert.Null(await f.Visits(session).Accept(f.Second.Id, offer.Id, offer.Version, default));
        using (var session = f.Session())
            await Assert.ThrowsAsync<ShiftConflictException>(() => f.Visits(session).Accept(f.Cleaner.Id, offer.Id, offer.Version - 1, default));
        BranchVisitDto accepted;
        using (var session = f.Session()) accepted = (await f.Visits(session).Accept(f.Cleaner.Id, offer.Id, offer.Version, default))!;

        Assert.Equal(BranchVisitService.StatusAssigned, accepted.Status); Assert.Equal(f.Cleaner.Id, accepted.EmployeeId);
        var mine = Assert.Single(await Mine(f, f.Cleaner));
        Assert.Equal(2, mine.VisitNumber); Assert.Equal("AdminPanel", mine.VisitSource); Assert.Equal("Windows too", mine.Notes);
        using (var session = f.Session()) Assert.Empty(await f.Visits(session).Offers(f.Cleaner.Id, default));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_cleaner_who_accepts_work_automatically_gets_the_visit_at_once()
    {
        using var f = await Fixture.Create();
        using (var writer = f.Store.LightweightSession(f.Tenant))
        {
            var cleaner = await writer.LoadAsync<Employee>(f.Cleaner.Id);
            cleaner!.AutoAcceptWork = true; writer.Store(cleaner); await writer.SaveChangesAsync();
        }
        BranchVisitDto visit;
        using (var session = f.Session()) visit = await f.Visits(session).Add(OnDay(f, employee: f.Cleaner.Id), default);

        Assert.Equal(BranchVisitService.StatusAssigned, visit.Status); Assert.Equal(f.Cleaner.Id, visit.EmployeeId); Assert.Null(visit.OfferedEmployeeId);
        Assert.Single(await Mine(f, f.Cleaner));
        using var read = f.Session();
        Assert.Empty(await f.Visits(read).Offers(f.Cleaner.Id, default));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Declining_leaves_the_visit_unassigned()
    {
        using var f = await Fixture.Create();
        BranchVisitDto visit;
        using (var session = f.Session()) visit = await f.Visits(session).Add(OnDay(f, employee: f.Cleaner.Id), default);
        PartnerCleanerOfferDto offer;
        using (var session = f.Session()) offer = Assert.Single(await f.Visits(session).Offers(f.Cleaner.Id, default));
        using (var session = f.Session()) Assert.True(await f.Visits(session).Decline(f.Cleaner.Id, offer.Id, offer.Version, default));

        using var read = f.Session();
        var after = Assert.Single((await f.Visits(read).Get(f.Branch.Id, Fixture.DayStart, Fixture.DayStart.AddDays(1), default))!.Visits);
        Assert.Equal(BranchVisitService.StatusUnassigned, after.Status);
        Assert.Empty(await f.Visits(read).Offers(f.Cleaner.Id, default));
        Assert.False(await f.Visits(read).Decline(f.Cleaner.Id, offer.Id, offer.Version, default));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Changing_the_cleaner_withdraws_a_waiting_offer_and_releases_an_accepted_one_until_work_starts()
    {
        using var f = await Fixture.Create();
        BranchVisitDto visit;
        using (var session = f.Session()) visit = await f.Visits(session).Add(OnDay(f, employee: f.Cleaner.Id), default);
        PartnerCleanerOfferDto firstOffer;
        using (var session = f.Session()) firstOffer = Assert.Single(await f.Visits(session).Offers(f.Cleaner.Id, default));

        // Waiting offer: the first cleaner's offer is withdrawn and the second cleaner is offered the visit.
        BranchVisitDto moved;
        using (var session = f.Session()) moved = await f.Visits(session).SetEmployee(visit.ShiftId, visit.Id, new(f.Second.Id, visit.OfferRevision), default);
        Assert.Equal(f.Second.Id, moved.OfferedEmployeeId);
        using (var session = f.Session())
        {
            Assert.Empty(await f.Visits(session).Offers(f.Cleaner.Id, default));
            Assert.Null(await f.Visits(session).Accept(f.Cleaner.Id, firstOffer.Id, firstOffer.Version, default));
        }
        // A stale screen cannot overwrite the newer choice.
        using (var session = f.Session())
            await Assert.ThrowsAsync<ShiftConflictException>(() => f.Visits(session).SetEmployee(visit.ShiftId, visit.Id, new(f.Cleaner.Id, visit.OfferRevision), default));
        PartnerCleanerOfferDto secondOffer;
        using (var session = f.Session()) secondOffer = Assert.Single(await f.Visits(session).Offers(f.Second.Id, default));
        BranchVisitDto held;
        using (var session = f.Session()) held = (await f.Visits(session).Accept(f.Second.Id, secondOffer.Id, secondOffer.Version, default))!;
        Assert.Single(await Mine(f, f.Second));

        // Accepted, not started: the holder is released and the visit is offered back to the first cleaner.
        BranchVisitDto back;
        using (var session = f.Session()) back = await f.Visits(session).SetEmployee(visit.ShiftId, visit.Id, new(f.Cleaner.Id, held.OfferRevision), default);
        Assert.Equal(BranchVisitService.StatusOffered, back.Status); Assert.Null(back.EmployeeId);
        Assert.Empty(await Mine(f, f.Second));
        PartnerCleanerOfferDto thirdOffer;
        using (var session = f.Session()) thirdOffer = Assert.Single(await f.Visits(session).Offers(f.Cleaner.Id, default));
        BranchVisitDto final;
        using (var session = f.Session()) final = (await f.Visits(session).Accept(f.Cleaner.Id, thirdOffer.Id, thirdOffer.Version, default))!;

        // Once work has started the cleaner cannot be changed.
        using (var writer = f.Store.LightweightSession(f.Tenant))
        {
            writer.Store(new QualityCycleForm { Id = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"), ShiftId = final.ShiftId, CompanyId = f.Company.Id,
                BranchId = f.Branch.Id, EmployeeId = f.Cleaner.Id, ShiftOccurrenceUtc = final.StartUtc, ShiftOccurrenceEndUtc = final.EndUtc, StartedAtUtc = final.StartUtc });
            await writer.SaveChangesAsync();
        }
        using (var session = f.Session())
        {
            var error = await Assert.ThrowsAsync<ShiftConflictException>(() => f.Visits(session).SetEmployee(visit.ShiftId, visit.Id, new(f.Second.Id, final.OfferRevision), default));
            Assert.Contains("Work has started", error.Message);
        }
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_partner_booking_is_not_reassigned_from_the_visit_screen_and_extras_are_added_only_through_add_visit()
    {
        using var f = await Fixture.Create();
        var booked = await f.Accepted("08:00", f.Cleaner);
        using var session = f.Session();
        var repository = new ShiftRepository(session);
        var visit = Assert.Single(await repository.ListOccurrenceExceptionsAsync(booked.LinkedShiftId!.Value, Fixture.DayStart, Fixture.DayStart.AddDays(1), default));
        await Assert.ThrowsAsync<ShiftConflictException>(() => f.Visits(session).SetEmployee(visit.ShiftId, visit.Id, new(f.Second.Id, visit.OfferRevision), default));
        var save = new ShiftHandlers.SaveShiftOccurrenceExceptionCommandHandler(repository, new EmployeeRepository(session), f.Profiles.Object, Fixture.Context(f.Tenant));
        var error = await Assert.ThrowsAsync<ShiftConflictException>(() => save.Handle(new(visit.ShiftId, null, null,
            Fixture.DayStart.AddHours(18), Fixture.DayStart.AddHours(19), ShiftOccurrenceExceptionStatus.Extra, "Extra"), default));
        Assert.Contains("Add visit", error.Message);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Invalid_visits_are_refused_with_a_reason()
    {
        using var f = await Fixture.Create();
        using var session = f.Session(); var visits = f.Visits(session);
        var backwards = await Assert.ThrowsAsync<ValidationException>(() => visits.Add(OnDay(f, "10:00", "08:00"), default));
        Assert.Contains("end time must be after the start time", backwards.Message);
        await Assert.ThrowsAsync<ValidationException>(() => visits.Add(OnDay(f, "8", "10:00"), default));
        await Assert.ThrowsAsync<ValidationException>(() => visits.Add(new(f.Branch.Id, Fixture.Day, Fixture.Day.AddDays(-1), "08:00", "10:00"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => visits.Add(new(Guid.NewGuid(), Fixture.Day, Fixture.Day, "08:00", "10:00"), default));
        await Assert.ThrowsAsync<ValidationException>(() => visits.Add(OnDay(f, employee: f.Partner.Id), default));
        // A visit must be billable: a property without a price cannot take one.
        var unpriced = Branch.Create(f.Company.Id, "No price yet"); unpriced.SetPropertyDetails(20, "Normal");
        using (var writer = f.Store.LightweightSession(f.Tenant)) { writer.Store(unpriced); await writer.SaveChangesAsync(); }
        var noPrice = await Assert.ThrowsAsync<ShiftConflictException>(() => visits.Add(new(unpriced.Id, Fixture.Day, Fixture.Day, "08:00", "10:00"), default));
        Assert.Contains("no cleaning price", noPrice.Message);
        using var reader = f.Reader();
        Assert.Empty(await reader.Query<Shift>().ToListAsync());
        Assert.Empty(await reader.Query<ShiftOccurrenceException>().ToListAsync());
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task An_admin_visit_is_invoiced_once_at_the_property_price_in_force_on_its_date()
    {
        using var f = await Fixture.Create();
        using (var writer = f.Store.LightweightSession(f.Tenant))
        {
            // 100 EUR from January, 150 EUR from 12 November. The visit on 10 November inherits 100.
            var branch = await writer.LoadAsync<Branch>(f.Branch.Id);
            branch!.SetPricing(150, 25.5m, 120, effectiveFrom: new DateOnly(2026, 11, 12)); writer.Store(branch); await writer.SaveChangesAsync();
        }
        BranchVisitDto early, late;
        using (var session = f.Session()) early = await f.Visits(session).Add(OnDay(f, employee: f.Cleaner.Id), default);
        using (var session = f.Session())
            late = await f.Visits(session).Add(new(f.Branch.Id, Fixture.Day.AddDays(3), Fixture.Day.AddDays(3), "08:00", "10:00", null, f.Cleaner.Id), default);
        foreach (var visit in new[] { early, late })
        {
            PartnerCleanerOfferDto offer;
            using (var session = f.Session()) offer = (await f.Visits(session).Offers(f.Cleaner.Id, default)).Single(x => x.RequestId == visit.Id);
            using (var session = f.Session()) await f.Visits(session).Accept(f.Cleaner.Id, offer.Id, offer.Version, default);
            using var writer = f.Store.LightweightSession(f.Tenant);
            writer.Store(new QualityCycleForm { Id = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"), ShiftId = visit.ShiftId, CompanyId = f.Company.Id, BranchId = f.Branch.Id,
                EmployeeId = f.Cleaner.Id, ShiftOccurrenceUtc = visit.StartUtc, ShiftOccurrenceEndUtc = visit.EndUtc, StartedAtUtc = visit.StartUtc,
                IsSubmitted = true, EndedAtUtc = visit.EndUtc, SubmittedUtc = visit.EndUtc });
            await writer.SaveChangesAsync();
        }
        var request = new ShiftInvoiceRequest(f.Company.Id, [early.ShiftId], new(2026, 11, 1), new(2026, 11, 30),
            new Address { Street = "Customer 1", PostalCode = "00100", City = "Helsinki" });
        using (var session = f.Session())
        {
            var invoices = f.Invoices(session);
            var preview = await invoices.PreviewAsync(request, default);
            Assert.Equal(new[] { 100m, 150m }, preview.Invoice.Lines.Select(l => l.NetAmount!.Value).Order().ToArray());
            Assert.Equal(new[] { early.StartUtc, late.StartUtc }, preview.Invoice.Lines.SelectMany(l => l.OccurrenceStartsUtc).Order().ToArray());
            await invoices.IssueAsync(request with { PreviewToken = preview.PreviewToken }, default);
        }
        // Both visits are on the issued invoice, so nothing is left to bill for the same period.
        using (var session = f.Session())
            await Assert.ThrowsAsync<ValidationException>(() => f.Invoices(session).PreviewAsync(request, default));
    }

    private static async Task<IReadOnlyList<MyShiftOccurrenceDto>> Mine(Fixture f, Employee cleaner)
    {
        using var read = f.Session();
        var handler = new QualityCycleHandlers.GetMyShiftsQueryHandler(new ShiftRepository(read), new QualityCycleRepository(read, f.Store),
            new EmployeeRepository(read), new ShiftWorkReservation(Fixture.Config(), read), f.Profiles.Object, Fixture.Context(f.Tenant));
        return await handler.Handle(new(cleaner.Id, Fixture.DayStart, Fixture.DayStart.AddDays(1)), default);
    }
}
