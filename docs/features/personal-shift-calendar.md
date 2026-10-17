# Personal shift calendar and deleted-shift history

Updated 2026-10-17. Employees, tenant managers, and admins with an active linked employee identity use the same `/me/shifts/` screen.

## Navigation

- Tabs are Past, Today, Future. Today is selected on every fresh visit and shows that tenant-local day's shifts directly.
- Past and Future group work into Monday-based weeks, with the most recent week/day first. Week and daily counts are visible together even when folded. Clicking a day opens its week and shifts; at most one week and one day are open.
- Activity selections, photo notes and general notes survive switching days/tabs in the current browser session. Successful submission clears the draft.
- Calendar dates use the tenant time zone. A tenant-midnight rollover refreshes the selected period. Starting still requires the scheduled local date; actual start/end timestamps are unchanged.
- Past includes recorded work even after the employee was unassigned, the shift deactivated, or the shift deleted. Deleted work is labelled as historical and cannot be started or edited.
- Deactivated/deleted shifts are excluded from Today/Future and unfinished personal work, including already-created checklist forms. Only completed visits on earlier tenant-local dates remain in Past history.

## API and paging

`GET /api/v1/me/shifts/calendar?view=today|past|future&cursor=yyyy-MM-dd` resolves the employee from the verified login, never a supplied employee ID. It returns `occurrences`, `timeZoneId`, `fromLocalDate`, and exclusive `toLocalDate`. The existing `/me/shifts` contract remains available.

Today covers one local calendar date. Past and Future load 31 local dates per page; Load earlier/later advances using the returned boundaries. UTC intervals remain below the existing 32-day query limit across DST. Failed extra pages retain loaded history and retry the same cursor. Late responses cannot replace a newly selected tab.

## Deleting inactive shifts

Assignments and QC records no longer block deletion. The live shift, roster links, and occurrence exceptions are removed atomically. A separate `DeletedShift` reporting snapshot preserves schedule/assignment history and all occurrence overrides; shift-specific historical exception reads use that archive. QC records retain their original historical identifier/name and get `ShiftDeletedUtc`, detaching them from active work while keeping checklist answers, notes, photos, and actual timestamps intact.

Monthly reports can read deleted-shift snapshots, including an explicit deleted shift filter. Old QC tokens cannot modify detached history. Active shifts must be deactivated first. Deletion remains scoped to the caller's tenant and existing admin/manager permissions.

Admin and manager Active/Inactive lists read only live shift documents. Reporting snapshots must never appear in either management tab or its counts. Deleting a shift therefore removes it from management lists while retaining it in historical reports.

## Regression coverage

- PostgreSQL deletion regression compares monthly report content before/after deletion, checks roster/exception unlinking, preserves photo notes, and verifies another tenant's matching records remain untouched.
- Backend tests cover DST calendar boundaries, adjacent pages, invalid cursors, historical employee ownership and all staff-role calendar access.
- Desktop/mobile Playwright covers default Today, folded counts, single-open week/day, ordering, paging failures/retry, stale responses, keyboard tabs, draft preservation, date rollover, confirmation cancellation and successful shift deletion.
