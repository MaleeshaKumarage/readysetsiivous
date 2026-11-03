using CleaningSuite.Application.WorkQueue;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Partners;
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

        var counts = new Dictionary<string, int>();
        foreach (var view in WorkQueueViews.All)
        {
            var count = 0;
            if (includeBookings) count += await BookingsIn(view).CountAsync(ct);
            if (includePartners) count += await PartnersIn(view).CountAsync(ct);
            counts[view] = count;
        }

        var completedFirst = query.View == WorkQueueViews.Completed;
        var skip = Math.Clamp(query.Skip, 0, WorkQueueLimits.MaxSkip);
        var window = skip + Math.Clamp(query.Take, 1, 100);
        var rows = new List<(DateTime Key, Guid Id, WorkQueueItem Item)>();

        if (includeBookings)
        {
            var bookings = BookingsIn(query.View);
            bookings = completedFirst ? bookings.OrderByDescending(b => b.StartUtc).ThenBy(b => b.Id) : bookings.OrderBy(b => b.StartUtc).ThenBy(b => b.Id);
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
            var partners = PartnersIn(query.View);
            partners = completedFirst ? partners.OrderByDescending(r => r.FromDate).ThenBy(r => r.Id) : partners.OrderBy(r => r.FromDate).ThenBy(r => r.Id);
            var requests = await partners.Take(window).ToListAsync(ct);
            var companyKeys = requests.Select(r => r.CompanyId).Distinct().ToArray();
            var branchKeys = requests.Select(r => r.BranchId).Distinct().ToArray();
            var companies = companyKeys.Length == 0 ? new Dictionary<Guid, Company>()
                : (await Db.Query<Company>().Where(c => companyKeys.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
            var branches = branchKeys.Length == 0 ? new Dictionary<Guid, Branch>()
                : (await Db.Query<Branch>().Where(b => branchKeys.Contains(b.Id)).ToListAsync(ct)).ToDictionary(b => b.Id);
            foreach (var request in requests)
            {
                companies.TryGetValue(request.CompanyId, out var company);
                branches.TryGetValue(request.BranchId, out var branch);
                var time = request.ExactStartLocalTime is null ? "" : " " + request.ExactStartLocalTime;
                rows.Add((request.FromDate.ToDateTime(TimeOnly.MinValue), request.Id, new WorkQueueItem(
                    "partner", request.Id, branch?.Name ?? "Property", company?.Name ?? "Customer",
                    request.FromDate.ToString("yyyy-MM-dd") + time, request.Status,
                    WorkQueueRules.PartnerLabel(request), null, "/admin/partner-requests/")));
            }
        }

        var ordered = completedFirst
            ? rows.OrderByDescending(r => r.Key).ThenBy(r => r.Id)
            : rows.OrderBy(r => r.Key).ThenBy(r => r.Id);
        var items = ordered.Skip(skip).Take(query.Take).Select(r => r.Item).ToList();

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

        IQueryable<PartnerCleaningRequest> PartnersIn(string view)
        {
            var q = Db.Query<PartnerCleaningRequest>().Where(WorkQueueRules.PartnerIn(view));
            if (search is not null) q = q.Where(r => companyIds.Contains(r.CompanyId));
            return q;
        }
    }
}
