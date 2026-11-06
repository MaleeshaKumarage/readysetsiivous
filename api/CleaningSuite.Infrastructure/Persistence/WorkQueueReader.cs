using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>Reads bounded pages from bookings and partner requests. Each source returns at most skip+take rows before merging.</summary>
public sealed class WorkQueueReader(ITenantSession tenant, ITenantProfileRepository profiles, ITenantContext context) : IWorkQueueReader
{
    private IDocumentSession Db => tenant.Session;

    public async Task<WorkQueuePage> ReadAsync(WorkQueueQuery query, CancellationToken ct = default)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var includeBookings = query.Source is "all" or "booking";
        var includePartners = query.Source is "all" or "partner";
        var includeShifts = query.Source is "all" or "shift";
        var assignee = query.Assignee;
        // Employee ids are GUIDs. A non-GUID assignee matches nothing in any source, so bookings and shifts treat it the same way.
        if (assignee is not null && !Guid.TryParse(assignee, out _)) { includeBookings = false; includeShifts = false; }
        var assigneeKey = assignee is not null && Guid.TryParse(assignee, out var parsedKey) ? parsedKey.ToString() : assignee;
        // Partner requests carry no assignee of their own, so an assignee filter leaves them out.
        if (assignee is not null) includePartners = false;
        // Unassigned work: occurrences and bookings with no cleaner. Partner requests have no assignee of their own, so they stay in.
        var unassignedOnly = query.UnassignedOnly;
        var fromDay = query.DateFrom is null ? (DateOnly?)null : DateOnly.ParseExact(query.DateFrom, "yyyy-MM-dd");
        var toDay = query.DateTo is null ? (DateOnly?)null : DateOnly.ParseExact(query.DateTo, "yyyy-MM-dd");
        bool InDates(DateOnly day) => (fromDay is null || day >= fromDay) && (toDay is null || day <= toDay);
        // A partner request is kept when its whole span overlaps the window, the same rule bookings and shifts use.
        bool SpanOverlaps(DateOnly start, DateOnly end) => (fromDay is null || end >= fromDay) && (toDay is null || start <= toDay);
        var companyIds = search is not null && (includePartners || includeShifts)
            ? (await Db.Query<Company>().Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).Select(c => c.Id).ToListAsync(ct)).ToArray()
            : Array.Empty<Guid>();

        // Partner visits are few per tenant and their view depends on their forms, so they are classified in memory.
        var partnerViews = includePartners ? (await ClassifyPartnersAsync(search, companyIds, ct)).Where(p => SpanOverlaps(p.Request.FromDate, p.Request.ToDate)).ToList() : new List<PartnerView>();
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, context, ct);
        var assigneeId = assignee is not null && Guid.TryParse(assignee, out var parsedAssignee) ? parsedAssignee : (Guid?)null;
        var shiftRows = includeShifts
            ? (await ClassifyShiftsAsync(search, companyIds, zone, ct))
                .Where(r => InDates(DateOnly.FromDateTime(r.Local)) && (assignee is null || (assigneeId is { } id && r.Visit.EmployeeIds.Contains(id))) && (!unassignedOnly || r.Visit.EmployeeIds.Count == 0))
                .ToList()
            : new List<ShiftRow>();

        var counts = new Dictionary<string, int>();
        foreach (var view in WorkQueueViews.All)
        {
            var count = 0;
            if (includeBookings) count += await BookingsIn(view).CountAsync(ct);
            count += partnerViews.Count(p => p.View == view);
            count += shiftRows.Count(r => r.View == view);
            counts[view] = count;
        }

        var closedFirst = query.View is WorkQueueViews.Completed or WorkQueueViews.Cancelled;
        var skip = Math.Clamp(query.Skip, 0, WorkQueueLimits.MaxSkip);
        var window = skip + Math.Clamp(query.Take, 1, 100);
        var rows = new List<(DateTime Key, Guid Id, WorkQueueItem Item)>();

        if (includeBookings)
        {
            var bookings = BookingsIn(query.View);
            bookings = closedFirst ? bookings.OrderByDescending(b => b.StartUtc).ThenBy(b => b.Id) : bookings.OrderBy(b => b.StartUtc).ThenBy(b => b.Id);
            foreach (var booking in await bookings.Take(window).ToListAsync(ct))
            {
                rows.Add((LocalKey(booking.StartLocalDate, booking.StartLocalTime) ?? booking.StartUtc, booking.Id, new WorkQueueItem(
                    "booking", booking.Id, booking.BookingNumber, booking.Customer.Name,
                    booking.StartLocalDate + " " + booking.StartLocalTime, booking.Status,
                    WorkQueueRules.BookingLabel(booking), booking.EmployeeName, "/admin/work-queue/?booking=" + booking.Id)));
            }
        }

        if (includePartners)
        {
            var ordered = partnerViews.Where(p => p.View == query.View);
            ordered = closedFirst ? ordered.OrderByDescending(p => p.Request.FromDate).ThenBy(p => p.Request.Id) : ordered.OrderBy(p => p.Request.FromDate).ThenBy(p => p.Request.Id);
            var page = ordered.Take(window).ToList();
            var companies = await CompaniesAsync(page.Select(p => p.Request.CompanyId), ct);
            var branches = await BranchesAsync(page.Select(p => p.Request.BranchId), ct);
            foreach (var (view, request) in page.Select(p => (p.View, p.Request)))
            {
                companies.TryGetValue(request.CompanyId, out var company);
                branches.TryGetValue(request.BranchId, out var branch);
                var time = request.ExactStartLocalTime is null ? "" : " " + request.ExactStartLocalTime;
                rows.Add((request.FromDate.ToDateTime(TimeOnly.MinValue), request.Id, new WorkQueueItem(
                    "partner", request.Id, branch?.Name ?? "Property", company?.Name ?? "Customer",
                    request.FromDate.ToString("yyyy-MM-dd") + time, request.Status,
                    WorkQueueRules.PartnerLabel(request, view), null, "/admin/partner-requests/")));
            }
        }

        if (includeShifts)
        {
            var ordered = shiftRows.Where(r => r.View == query.View);
            ordered = closedFirst ? ordered.OrderByDescending(r => r.Local).ThenBy(r => r.Shift.Id) : ordered.OrderBy(r => r.Local).ThenBy(r => r.Shift.Id);
            var page = ordered.Take(window).ToList();
            var companies = await CompaniesAsync(page.Select(r => r.Shift.CompanyId), ct);
            foreach (var row in page)
            {
                companies.TryGetValue(row.Shift.CompanyId, out var company);
                rows.Add((row.Local, row.Id, new WorkQueueItem(
                    "shift", row.Id, row.Shift.Name, company?.Name ?? "Customer",
                    row.Local.ToString("yyyy-MM-dd HH:mm"), row.View,
                    row.Label, null, "/admin/shifts/calendar/")));
            }
        }

        var merged = closedFirst
            ? rows.OrderByDescending(r => r.Key).ThenBy(r => r.Id)
            : rows.OrderBy(r => r.Key).ThenBy(r => r.Id);
        var items = merged.Skip(skip).Take(query.Take).Select(r => r.Item).ToList();

        return new WorkQueuePage(query.View, query.Source, items, counts[query.View], counts);

        // Both sources merge on the tenant-local wall clock. Bookings use their stored local start; partner requests
        // are date windows ordered by their first day (as the database orders them), so they sort at local midnight.
        // Using UTC for bookings would shift them against partner rows by the tenant's UTC offset.
        static DateTime? LocalKey(string date, string time) =>
            DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day)
                ? day.ToDateTime(TimeOnly.TryParseExact(time, ["HH:mm", "HH:mm:ss"], out var at) ? at : TimeOnly.MinValue)
                : null;

        IQueryable<Booking> BookingsIn(string view)
        {
            var q = Db.Query<Booking>().Where(WorkQueueRules.BookingIn(view));
            if (search is not null) q = q.Where(b => b.BookingNumber.Contains(search, StringComparison.OrdinalIgnoreCase) || b.Customer.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
            // Booking StartUtc is a timestamp without time zone, so the bounds are Unspecified-kind (Npgsql rejects Kind=Utc).
            // Local midnight on 0001-01-01 is before DateTime.MinValue once converted to UTC, so an open start stays open.
            if (fromDay is { } firstDay && firstDay != DateOnly.MinValue) { var lower = TenantTimeZone.StartOfLocalDay(firstDay.ToDateTime(TimeOnly.MinValue), zone); var unspecifiedLower = DateTime.SpecifyKind(lower, DateTimeKind.Unspecified); q = q.Where(b => b.StartUtc >= unspecifiedLower); }
            // The day after 9999-12-31 does not exist, so an end date at the maximum leaves the upper bound open.
            if (toDay is { } lastDay && lastDay != DateOnly.MaxValue) { var upper = TenantTimeZone.StartOfLocalDay(lastDay.AddDays(1).ToDateTime(TimeOnly.MinValue), zone); var unspecifiedUpper = DateTime.SpecifyKind(upper, DateTimeKind.Unspecified); q = q.Where(b => b.StartUtc < unspecifiedUpper); }
            if (assignee is not null) q = q.Where(b => b.EmployeeId == assigneeKey);
            if (unassignedOnly) q = q.Where(b => b.EmployeeId == null);
            return q;
        }
    }

    private sealed record PartnerView(string View, PartnerCleaningRequest Request);

    private sealed record ShiftRow(string View, string Label, Shift Shift, ShiftVisitRosterEntry Visit, DateTime Local, Guid Id);

    /// <summary>
    /// Company shifts (not partner-created ones, which appear as partner requests). Each occurrence in the bounded
    /// window is classified from its roster entry and its forms.
    /// </summary>
    private async Task<List<ShiftRow>> ClassifyShiftsAsync(string? search, Guid[] companyIds, TimeZoneInfo zone, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = TenantTimeZone.LocalDate(now, zone);
        var fromUtc = TenantTimeZone.StartOfLocalDay(today.AddDays(-WorkQueueLimits.ShiftHistoryDays), zone);
        var toUtc = TenantTimeZone.StartOfLocalDay(today.AddDays(WorkQueueLimits.ShiftFutureDays + 1), zone);
        var shiftRepository = new ShiftRepository(tenant);
        var shifts = (await shiftRepository.ListIncludingInactiveAsync(ct))
            .Where(shift => shift.PartnerRequestId is null && (search is null || companyIds.Contains(shift.CompanyId))).ToArray();
        if (shifts.Length == 0) return new List<ShiftRow>();
        var shiftIds = shifts.Select(shift => shift.Id).ToArray();
        // Occurrence columns hold Unspecified-kind values; Npgsql rejects Kind=Utc bounds against them.
        var fromKey = DateTime.SpecifyKind(fromUtc, DateTimeKind.Unspecified);
        var toKey = DateTime.SpecifyKind(toUtc, DateTimeKind.Unspecified);
        var forms = await Db.Query<QualityCycleForm>()
            .Where(f => shiftIds.Contains(f.ShiftId) && f.ShiftDeletedUtc == null && f.ShiftOccurrenceUtc >= fromKey && f.ShiftOccurrenceUtc < toKey)
            .ToListAsync(ct);
        var formsByOccurrence = forms.GroupBy(f => (f.ShiftId, f.ShiftOccurrenceUtc))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QualityCycleForm>)g.ToList());
        var rows = new List<ShiftRow>();
        foreach (var shift in shifts)
        {
            var visits = await shiftRepository.ResolveRosterAsync(shift, fromUtc, toUtc, zone, ct, includeCancelled: true);
            foreach (var visit in visits)
            {
                var start = visit.Occurrence.StartUtc;
                var evidence = formsByOccurrence.TryGetValue((shift.Id, start), out var found) ? found : Array.Empty<QualityCycleForm>();
                var view = WorkQueueRules.ShiftView(visit, evidence, now);
                rows.Add(new ShiftRow(view, WorkQueueRules.ShiftLabel(visit, view, now), shift, visit, TimeZoneInfo.ConvertTimeFromUtc(start, zone), StableOccurrenceId(shift.Id, start)));
            }
        }
        return rows;
    }

    private static Guid StableOccurrenceId(Guid shiftId, DateTime startUtc) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"shift-occurrence:{shiftId:N}:{startUtc.Ticks}")).AsSpan(0, 16));

    private async Task<List<PartnerView>> ClassifyPartnersAsync(string? search, Guid[] companyIds, CancellationToken ct)
    {
        var q = Db.Query<PartnerCleaningRequest>().AsQueryable();
        if (search is not null) q = q.Where(r => companyIds.Contains(r.CompanyId));
        var requests = await q.ToListAsync(ct);
        var shiftIds = requests.Where(r => r.LinkedShiftId.HasValue).Select(r => r.LinkedShiftId!.Value).Distinct().ToArray();
        var forms = shiftIds.Length == 0 ? new List<QualityCycleForm>()
            : await Db.Query<QualityCycleForm>().Where(f => shiftIds.Contains(f.ShiftId)).ToListAsync(ct);
        var formsByShift = forms.GroupBy(f => f.ShiftId).ToDictionary(g => g.Key, g => (IReadOnlyList<QualityCycleForm>)g.ToList());
        var classified = new List<PartnerView>();
        foreach (var request in requests)
        {
            var visitForms = request.LinkedShiftId is { } shift && formsByShift.TryGetValue(shift, out var found) ? found : Array.Empty<QualityCycleForm>();
            var view = WorkQueueRules.PartnerView(request, visitForms);
            if (view is not null) classified.Add(new PartnerView(view, request));
        }
        return classified;
    }

    private async Task<Dictionary<Guid, Company>> CompaniesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var keys = ids.Distinct().ToArray();
        return keys.Length == 0 ? new() : (await Db.Query<Company>().Where(c => keys.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
    }

    private async Task<Dictionary<Guid, Branch>> BranchesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var keys = ids.Distinct().ToArray();
        return keys.Length == 0 ? new() : (await Db.Query<Branch>().Where(b => keys.Contains(b.Id)).ToListAsync(ct)).ToDictionary(b => b.Id);
    }
}
