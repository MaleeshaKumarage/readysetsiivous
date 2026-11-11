# Branch visit shifts as occurrences (draft for review)

Status: DRAFT. Nothing implemented. Decisions marked **[decide]** need the user's answer before build starts.

## Problem

Every accepted partner booking request materialises a brand-new `Shift` (`PartnerService.MaterializeAcceptance`, id = `StableId("partner-shift:{requestId}")`, schedule `OnCallFlexible`). Admins creating one-time visits (2026-11-03 one-time design) also create a new shift per visit. A branch therefore accumulates many shifts with the same address, which makes the shift list, quality-cycle reports and invoice shift selection hard to read and the "most recent shift" hard to find.

## Target model

- **One main visit shift per branch** for on-call / one-time work (`Shift` with `Schedule.Type = OnCallFlexible`, or the one-time family as decided below).
- **Each booking, partner request or admin-entered visit is an occurrence** on that main shift, not a new shift.
- Occurrences use the existing `ShiftOccurrenceException` pipeline with status `Extra` (already used today by partner acceptance). Each occurrence carries its own date, start/end and notes.
- Pricing, company, branch, tax and billing stay on the main shift and its branch price history (`ShiftBillingProfile.BranchPriceEffectiveFrom`). Occurrences do not copy price.
- Recurring schedules (Daily, Weekly, BiWeekly, Monthly) are untouched.

Existing infrastructure already covers most of this: Extra occurrences, `ShiftOccurrenceAssignment` (Replace / Add per visit), visit-priced completion, and the quality-cycle form keyed by occurrence start. The change is mainly in *where* acceptance and admin creation write, plus the places that assume one shift = one request.

## Employee side: unchanged

Employees still see the shift name, the visit date/time and the request state. The API shape they read (`ShiftOccurrenceDto` inside their own calendar) stays the same; only the underlying shift id is shared.

## Changes required

### Backend
1. **Find-or-create the branch main shift** (new application service). Key: `(CompanyId, BranchId, Kind = on-call main)`. Create on first booking; reuse afterwards. Enforce uniqueness in the existing reservation/transaction so two concurrent acceptances cannot create two mains.
2. **Partner acceptance** (`MaterializeAcceptance`): attach an Extra occurrence to the main shift with `StableId("partner-occurrence:{requestId}")` (already the case) instead of creating a shift. Stop writing `PartnerRequestId`, `ValidFrom/ValidUntil` per request and per-request `Notes` onto the shift; widen validity to cover all occurrences, or drop validity for on-call mains.
3. **Per-visit data that currently lives on the shift** must move to the occurrence or be derived from the company: `RequiredPhotoLabels`, `PartnerAccessInstructions`, contact fields, visit notes, quality-cycle template. Audit each usage before moving.
4. **Assignments**: today `ShiftAssignment` is whole-shift and acceptance ends all previous assignments for the shift (`foreach previous ... EndFrom`). On a shared main shift this would remove employee A when employee B accepts another visit. Acceptance must assign the employee to that occurrence only (`ShiftOccurrenceAssignment`, Replace), not end whole-shift assignments.
5. **Cancellation, reschedule, deletion of a request** must remove or cancel its own occurrence, never the main shift. `EnsureUnstartedUnbilled` guards stay per occurrence.
6. **Admin creation** ("one-time" / on-call visit): new action "Add visit to branch" that appends an occurrence to the branch main. Existing "New shift" flow for recurring schedules stays. Dates may be a single date/time or a range, creating one occurrence per day (the user's "4 times per week" case).
7. **Billing**: invoices for visit-priced shifts already key lines by `ShiftId` and resolved occurrence start, so a single main shift works. Verify duplicate-period checks still block overlapping periods for the same branch and that cancelled/extra/moved visits are counted once.
8. **Reports** (quality-cycle, shift list): group by branch main; list occurrences under it. Historical per-request shifts keep working.
9. **Overlap / conflict checks** operate on occurrence times, not shift validity windows.

### Migration / history **[decide]**
- Option A (recommended): **forward-only**. Existing per-request shifts stay as-is and are only read. New bookings go to the main. No data rewrite on production.
- Option B: one-off consolidation script that folds historical per-branch shifts into their main, moving occurrences, assignments and quality-cycle history. High risk: completed forms, invoices and report snapshots reference the old shift ids, so it needs a read-only dry run, private backup and verification.

### Frontend
- Admin: branch page / shift list shows one "Visits" entry per branch; "Add visit" dialog with date or date range + time. Keep company/branch selection and assignment controls as today.
- Partner portal and employee views: no change to wording or layout.
- Shift list / report pages: one row per branch main, occurrences listed under it.

## Decisions (answered by user, 2026-11-11)

1. **Historical data: merge existing shifts into one per branch.** Needs a read-only dry run, a private backup and verification before any production write. Completed forms, invoices and report snapshots reference old shift IDs, so they must be re-pointed or preserved. This is a separate, later PR, gated on the dry run.
2. **One main shift per branch by default.** A second on-call shift for the same branch is allowed only when an admin creates it on purpose.
3. **One-time visits retired for new records.** Admin one-time visits become occurrences on the branch main. The `OneTime` schedule type is no longer offered for new records. This supersedes the 2026-11-03 one-time design for new visits; existing one-time shifts are kept and readable.
4. **Per-visit details on the occurrence.** Notes and required photo sections live on each occurrence. Company access and contact details stay on the branch main.

## Occurrence naming and source (user request, 2026-11-11)

- Each occurrence has a generated **name** so people can tell visits apart. The address, size and service details stay on the main shift name and are not repeated per occurrence.
- Name format: `#<sequence> · <date or date range> · <source>`, for example `#3 · Mon 21 Apr · Admin panel` or `#5 · 25–27 Apr · Partner portal`.
- **Sequence** is a counter per branch main, starting at 1 and increasing with each new occurrence in the order they are created. It is not reset per month or per week. Stored on the occurrence when created so the name stays stable.
- **Source tag** is `Admin panel` or `Partner portal`, recording where the occurrence came from.
- A date range creates **one occurrence with one sequence number** covering the whole range (confirmed 2026-11-11). Exact dates use the same format with a single date. This replaces the earlier "one occurrence per day" plan for admin date ranges.
- Names are shown to admins and employees alike. Employees still see the shift name as the main title, with the occurrence name and their visit time underneath.

## Open items

- **Sequence after cancel or delete:** numbers are never reused (confirmed 2026-11-11). A removed `#4` leaves a gap, and the next new occurrence takes the next number above the highest one used.
- No open questions remain on naming.
- Build order below still applies, with the merge of historical shifts moved to the final chunk.

## Test plan (required by CLAUDE.md publishing rule)

- Backend (real PostgreSQL where persistence matters): find-or-create is idempotent and race-safe; two accepted requests produce two occurrences on one shift; accepting a second request does not end the first employee's assignment; cancel of one request leaves the other intact; admin date range creates one occurrence per day; billing counts each occurrence once and blocks overlapping invoice periods; historical shifts remain readable.
- Playwright on `chromium-desktop` and `mobile-touch`: partner accepts two requests and both appear on one branch shift; admin adds a date-range visit to the branch; employee still sees the shift name and requested time; cancel of one occurrence does not affect the other; existing specs that assert "new shift per request" are updated, not deleted.
- Focused runs: `npx playwright test <spec> --config playwright.config.ts` from `site/`, with pass counts reported.

## Delivery

One PR per chunk, in this order, each green before the next:
1. Main-shift find-or-create + partner acceptance writing occurrences (backend + tests).
2. Assignment and cancel semantics per occurrence (backend + tests).
3. Admin "add visit to branch" with date range (backend + Playwright).
4. Reporting/list grouping and invoice verification.
5. Optional: historical consolidation, only if approved after a dry run.

## Employee assignment and offers (user request + answers, 2026-11-11)

- A branch main shift has **one default employee**. Teams are set on individual occurrences, not on the main.
- Creating an occurrence on a shift with a default employee **creates an offer** to that employee for the new occurrence. Automatic acceptance (existing A01 feature) adds it directly, with no action. Otherwise the employee accepts the offer, and the visit then appears under their name as a normal shift.
- A shift with **no default employee** creates occurrences unassigned. Nobody is added automatically.
- An admin can **change the employee on an occurrence** at any time. The occurrence's own assignee overrides the shift default. Changing it later does not change the shift default.
- **Pending offer, admin changes employee:** the first employee's pending offer is withdrawn, and the new employee receives a fresh offer. Only the accepting employee holds the visit.
- **Accepted, not started, admin changes employee:** allowed. The former employee is released and notified. Once work has started, reassignment is blocked, as today.
- Offers and reassignments run through the existing shift reservation and version checks, so concurrent accept and reassign cannot both succeed.

## Deep check: invoicing, migration, PDFs and price visibility (2026-11-11)

### Invoicing
- Billing profiles, price plans and per-visit overrides are keyed by `ShiftId` (`ShiftBillingProfile`). Overrides match on `OccurrenceStartUtc`. Two visits with the same start on one main would collide. Merge must check for this.
- The duplicate-invoice guard (`ShiftInvoiceService`, `Billed(i, id)`) compares shift IDs only. If a visit billed under an old shift is re-billed under the main, it is billed twice. Fix: a shift alias map (old ID -> main ID) that the guard and the billing lookups resolve through.
- Legacy shifts keep their own `Plans`. Merging shifts with different rates needs a per-occurrence price, or the merge must refuse. The dry run has to list these.
- Completion evidence (`QualityCycleForm` keyed by `ShiftId` + `ShiftOccurrenceUtc`) must not be rewritten. Completed forms are evidence. Reads go through the alias map instead.
- `PartnerCleaningRequest.LinkedShiftId` and the stable occurrence IDs must resolve through the same alias map.

### Migration (startup, not a manual script)
- Use the existing `StartupRepairService` pattern: a runner with a mode setting (`DataCorrections:BranchShiftMerge:Mode` = Off / Report / Apply). Report logs the planned merge on each start. Apply performs it.
- Each repair is isolated and idempotent. A failure is logged and retried on the next start.
- Apply writes a backup document for every affected record in the same unit of work as the change, and records each merge in a mapping document. A second start finds the mapping and does nothing.
- Every replica runs startup repairs. Apply must take the tenant reservation (`IShiftWorkReservation`) so two replicas cannot merge at once.
- Mode is switched to Apply only after the Report output has been reviewed.

### Shift PDFs
- Quality-cycle report PDF: shows shift name and evidence by date. No price fields. Needs the occurrence number and source tag added to each evidence card. The sequence number must be stored on the form or joined from the occurrence.
- Invoice PDF: lines show shift name, occurrences and scheduled dates. Add the occurrence numbers (#n) to each line so the customer can match visits. Prices are customer-facing here by design.
- Shared report links and their references use the old shift ID. Existing shares keep their snapshots; new ones use the main.

### Price visibility to employees (must hold)
- Audited employee-reachable responses: `ShiftDto` (Schedule has no price fields), `ShiftOccurrenceDto`, `MyShiftOccurrenceDto` (quality-cycle form, notes, photo labels, access instructions, contact), `OwnWorkResponse`, `PartnerCleanerOfferDto` (no price), and the employee accept route `/api/v1/me/partner-offers/{id}/accept`, which returns only Id, Version, Status, LinkedShiftId and planned times.
- Not visible to employees: `PartnerRequestDto` (PriceNet, VatRatePercent), `ShiftBillingProfile`, invoices, and branch pricing. These are served only by operations routes (`/api/v1/admin/partner/*`, `/api/v1/manager/partner/*`).
- Rule for this change: occurrence and main-shift DTOs must never gain billing fields. Add a test that calls each employee route and asserts no `price`, `rate`, `vat` or `net` keys appear in the JSON.
- Caveat: a user who holds both an employee and a manager role receives manager responses. Confirm this is acceptable before build.

### Decision (2026-11-11)
- Confirmed by the product owner: a user who holds both employee and manager roles may see manager responses, including partner prices. No change to price visibility rules for this case.

## As built (2026-11-11)

### How it works
- **Main shift:** `Shift.IsBranchMain`, with an id derived from the branch (`Shift.BranchMainId`). Created on the first booking or added visit. Callers hold the tenant work reservation, so a branch cannot get two.
- **Visit:** an `Extra` `ShiftOccurrenceException` on the main shift carrying `Sequence`, `Source`, `Notes`, `RequiredPhotoLabels`, `PartnerRequestId`, and its offer (`OfferedEmployeeId`, `OfferId`, `OfferRevision`). The cleaner is the visit's `EmployeeId`. Main shifts take no whole-shift assignment.
- **Numbering:** `Shift.OccurrenceCounter` only goes up. A booking keeps its number when it is moved (`PartnerCleaningRequest.OccurrenceSequence`).
- **Same start time:** work records and invoice lines find a visit by shift and start. Two visits of one main shift therefore never share a start: the later one is stored one second later. The planned time shown to the partner is unchanged.
- **Partner bookings:** `PartnerService` writes, moves and removes only the booking's own visit. Bookings that already own a shift keep it until the startup merge.
- **Staff visits:** `BranchVisitService` and `api/v1/{admin|manager}/branch-visits`. One date or a date range is one visit. A property needs a price before it can take a visit.
- **Offers:** the chosen cleaner, or the shift's default cleaner (`Shift.DefaultEmployeeId`), gets an offer. Automatic acceptance assigns at once. Cleaners see and answer these offers through the existing `me/partner-offers` endpoints, so the cleaner app needed no new screen.
- **Invoicing:** a main shift is priced per visit. A partner booking uses the price and VAT frozen on its request; a staff visit uses the property price in force on its date. Each invoice line records the visit starts and numbers it bills (`InvoiceLine.OccurrenceStartsUtc`, `VisitNumbers`), and a main shift is blocked per visit, not per period.
- **Reports and PDFs:** the invoice PDF prints "Visit numbers: #n". The quality report and its photo appendix print "Visit: #n · source".
- **Price visibility:** `BranchVisitDto`, `BranchMainDto`, `ShiftOccurrenceDto` and `MyShiftOccurrenceDto` are covered by the reflection test that forbids money fields. Cleaner pages are checked in Playwright.

### Startup merge
- `BranchShiftMergeRunner`, run by `StartupRepairService` on every API start.
- **Default mode is Report:** it logs the plan and changes nothing. Set `DataCorrections:BranchShiftMerge:Mode=Apply` to perform the merge on the next start. `Off` skips it.
- Merges accepted partner bookings that own a shift. Removes leftover shifts of cancelled or re-opened bookings that have no work and no invoice. Keeps and logs anything unusual: work recorded at another time, two bookings at exactly the same start, hand-made roster changes, a property without size and cleaning type.
- One shift is one commit under the tenant work reservation, with a `BranchShiftMergeRecord` backup under the old shift's id.
- A visit invoiced before the merge stays billed through `ShiftOccurrenceException.LegacyShiftId`. Issued invoices are not rewritten. Moved work records keep `OriginalShiftId`.

### Differences from the plan
- **Admin-created one-time shifts are not merged.** The merge covers partner booking shifts only. Existing one-time shifts stay as they are and remain editable.
- **One-time is retired in the Add shift form only.** The API still accepts a one-time shift, so existing clients and data keep working.
- **No alias map.** Work records are re-pointed to the main shift and remember their original shift, which keeps every reader unchanged.
- **The manager page has no Add visit screen yet.** The manager API routes exist and are covered by the permission test.
- **Per-occurrence auto-accept was not built.** Automatic acceptance stays a setting of each cleaner.
