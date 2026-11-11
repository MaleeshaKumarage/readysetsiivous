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

## Open decisions **[decide]**

1. **Historical data**: forward-only (recommended) or consolidate existing shifts?
2. **One main per branch strictly**, or allow an admin to create a second on-call shift for the same branch when they need different terms? Recommend: one by default, second allowed only by explicit admin action.
3. **One-time family**: the 2026-11-03 one-time design creates a separate shift per admin-entered visit. Should admin one-time visits now become occurrences on the main (recommended, matches your request) and the `OneTime` schedule type be retired for new records? That supersedes part of the approved one-time design and needs your explicit OK.
4. **Visit-level fields**: confirm that per-visit notes, photo sections and request-specific instructions should live on the occurrence (recommended), while company access/contact details stay on the main.

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
