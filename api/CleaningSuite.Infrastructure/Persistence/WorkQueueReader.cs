using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>Reads bounded pages from bookings and partner requests. Each source returns at most skip+take rows before merging.</summary>
public sealed class WorkQueueReader(ITenantSession tenant) : IWorkQueueReader
{
    private IDocumentSession Db => tenant.Session;

    public async Task<WorkQueuePage> ReadAsync(WorkQueueQuery query, CancellationToken ct = default)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var includeBookings = query.Source is "all" or "booking";
        var includePartners = query.Source is "all" or "partner";
        var companyIds = search is not null && includePartners
            ? (await Db.Query<Company>().Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).Select(c => c.Id).ToListAsync(ct)).ToArray()
            : Array.Empty<Guid>();

        // Partner visits are few per tenant and their view depends on their forms, so they are classified in memory.
        var partnerViews = includePartners ? await ClassifyPartnersAsync(search, companyIds, ct) : new List<PartnerView>();

        var counts = new Dictionary<string, int>();
        foreach (var view in WorkQueueViews.All)
        {
            var count = 0;
            if (includeBookings) count += await BookingsIn(view).CountAsync(ct);
            count += partnerViews.Count(p => p.View == view);
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
            return q;
        }
    }

    private sealed record PartnerView(string View, PartnerCleaningRequest Request);

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
