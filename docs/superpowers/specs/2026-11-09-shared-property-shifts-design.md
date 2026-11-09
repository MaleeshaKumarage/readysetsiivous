# Shared property shifts: one model for partner and admin bookings

Status: draft for review. No code changes until approved.

## Problem

Partner acceptance and admin booking create visits for the same property in different shapes:

- Partner acceptance (`PartnerService.MaterializeAcceptance`) creates an On-call shift (type 4) named `Cleaning — {branch}`, with one `Extra` occurrence. A request without a start time spans the whole date range.
- Admin booking creates a One-time shift (type 6) with a typed name, or an On-call shift plus a separate dated visit.

Result: the same property can have two separate shifts, names and labels differ by source, and a cleaner overlap blocks the save with an error.

## Decisions (from review)

1. **One shared shift per property.** Partner and admin both attach visits to the same shift. No separate bookings.
2. **Overlaps never block.** A cleaner overlap shows a warning. Property overlaps are allowed.
3. **Name format** is generated, not typed:
   `{Deep|Normal} Cleaning - {Street}, {PostalCode} {City} |{SizeM2}m2|{Rooms}H|{Beds}V`
   Example: `Deep Cleaning - Mannerheimintie 1, 00100 Helsinki |34m2|3H|3V`
4. **Two new property fields**, required when a shift is created: `SizeM2` (integer, > 0) and `CleaningType` (`Deep` | `Normal`). Rooms (`RoomCount`) and beds (`BedCount`) already exist.
5. **Partner requests without a start time:** the admin enters the start and end time when accepting. The request keeps its date range; the accepted visit gets the chosen time.
6. **Migrate existing records:** partner and admin shifts move to the shared model (name, source tag). Price, invoice lines, completed visits and history are not rewritten.
7. **Source tag** `Partner` | `Admin` is shown to all roles on the admin calendar, shift list and the cleaner's My shifts.

## Model

- `Shift` gains `Source` (`Partner` | `Admin`) and a generated `DisplayName`. `Source` is display-only; it never changes billing or assignment.
- All new visits are one-time (type 6): date, start, end, one visit. Existing type 4 shifts keep working for reads and edits until migrated.
- `Branch` gains `SizeM2` and `CleaningType`. Both are validated in the domain (`SetPropertyDetails`).
- Name builder is one shared function, used by partner acceptance and admin create/update.

## Overlap behaviour

`VisitAssignmentSafety.CheckConflicts` no longer throws. It returns warnings listing the clashing visit (same wording as today) and the save proceeds. The response carries the warnings so the UI can show them.

## Migration

- Add `SizeM2` and `CleaningType` as nullable on existing branches. A branch without both values is shown as "Needs property details" and cannot receive new shifts until filled in.
- Rename display names of existing shifts only after their branch has both values.
- Set `Source` from the partner request link (`PartnerRequestId`) where present, else `Admin`.
- Migration is idempotent and writes an audit record per shift (`DataCorrectionRecord`).
- Invoices, `ShiftBillingProfile`, and completed visits are not touched.

## Tests

Backend (xUnit):
- Name builder: both values present, missing values rejected, Deep/Normal formatting.
- Partner acceptance without time requires admin-entered start and end.
- Overlap returns warnings and still saves, for cleaner and property overlaps.
- Migration is idempotent and leaves billing/invoice data unchanged.

Playwright (`chromium-desktop` and `mobile-touch`):
- Admin create shows generated name and Source tag.
- Partner accept with no time asks for start and end.
- Overlap shows a warning and the save succeeds.
- Existing shift still opens and edits after migration.

## Open risks

- Migration touches live shifts that have invoices. Run on a copy first; keep the audit record.
- Branches without `SizeM2`/`CleaningType` cannot take new shifts until filled in. Admins need a clear prompt.
- Existing type 4 partner shifts keep old behaviour until migration runs.
