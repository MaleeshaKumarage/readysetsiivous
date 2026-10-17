# Branded cleaning quality reports

## Approved scope

Adapt the supplied inspection-report reference into an A4 monthly cleaning activity report using the ReadySetSiivous logo, navy/gold palette, bordered tables and clear section bands. Keep existing activity measurements and the dated photo/note appendix. Do not add inspection ratings, invoicing, charges, signatures or new cleaner inputs. Assignment enhancements remain a separate workstream.

## Report layout

1. Repeating header: primary logo on white, Cleaning Quality Report title, month and tenant timezone. Use a deterministic report reference based on existing company/branch/shift IDs and month; do not imply a new persisted document ID.
2. Property details: company, branch, formatted branch address where available, and shift. Each shift starts a new section/page to preserve separate recurring-service reports. Missing address displays Not recorded; never invent values.
3. Monthly summary: scheduled visits/hours, recorded visits, missing submissions and pending visits, retaining current definitions. Show Recorded staff hours separately: sum each submitted work record's actual duration, never earliest-start/latest-end elapsed visit time. Deduplicate source forms by form ID before projection. A valid pair has both timestamps and end >= start; exclude missing/invalid pairs from the sum. With no valid pair, display Not available rather than zero. Display the count of incomplete timing records (started or submitted records without a valid submitted pair) beside any partial total. For two cleaners each recording two hours, staff hours are four while a two-hour scheduled visit remains two scheduled hours. If one cleaner has no end time, show two recorded staff hours with one incomplete timing record. Internal/shared PDFs consume the same projected calculations.
4. Activity table: activity, distinct days performed and recorded occurrences. Unchecked items are not automatically failures. Use existing aggregates without changing their meaning.
5. Visit table: one row per existing scheduled/reported occurrence with tenant-local scheduled date/time, SubmittedTasks/ExpectedTasks and submission status. Follow with work-record timing rows (one per deduplicated source form) showing actual start/end and duration or Not available, labelled by scheduled visit date/time. Internal downloads may show existing cleaner names on work-record rows. Public reports retain existing structured identity redaction and omit employee names/IDs, form IDs and QC tokens. Notes/photos remain unchanged user-supplied content; this does not introduce free-text or image identity sanitisation.
6. Appendix: preserve every submitted photo with its own note, scheduled shift date/time, company, branch and shift. Preserve standalone shift notes. Keep the existing safe image decoder and unavailable-image message. Fit images without cropping or distortion.
7. Footer: generated timestamp and Page X of Y, including appendix pages.

## Implementation boundaries

- Keep QuestPDF and the existing report endpoints. Introduce a small shared PDF theme/layout helper and bundle the approved existing logo as an embedded resource so rendering works offline in containers.
- Use site/public/brand/readyset-primary-logo.png and the navy/gold values from site/src/ds/tokens.ts. Use a legible font available to the renderer, white paper, light section fills and high-contrast text; colour must not be the only status indicator.
- Extend report DTOs with optional address and public-safe work-record timing/aggregate fields derived from existing records. New snapshot fields must be nullable/absent-aware. No database migration or new data entry. Safe work-record rows omit employee identity, form identifiers and submission tokens; shared snapshots continue to clear Forms.
- The monthly report service supplies branch address and safe work-record timing rows before sharing snapshots. Direct PDFs may add internal cleaner labels to these projected rows; shared PDFs use the same rows without those labels. Existing snapshot JSON lacking these optional fields remains readable: show its existing occurrence rows, label staff hours/timing Not available and never load live data into old snapshots.
- Reuse the shared header/table/footer styling for the legacy summary generator as well. Do not change its existing API contract or claim tenant-local dates where only UTC is supplied.
- Existing public links retain 30-day expiry/revocation and snapshot contents. Downloaded PDFs use the updated renderer; historical shares receive the new styling with only their previously captured data. No authorisation changes.

## Edge cases and validation

- Empty month: branded empty-state report, no fabricated metrics. Multiple branches/shifts remain clearly separated.
- Long names, addresses, activity descriptions, notes and hundreds of rows wrap and paginate without clipping. Table headers repeat on continuation pages; no orphan section headings. Finnish characters remain legible.
- Render and visually inspect representative populated, long-content, empty and legacy-snapshot PDFs. Check logo clarity, margins, table borders, page numbering, image aspect ratio and notes crossing pages.
- Backend PDF/report tests verify activity counts, scheduled-versus-staff-hour labels, two-cleaner totals, one incomplete pair, duplicate form IDs, invalid timing pairs, tenant-local DST/month boundaries, address fallback, image appendix retention and old snapshot compatibility. Public-share tests verify no structured private cleaner identity or QC tokens in shared JSON/PDF and identical internal/shared staff-hour calculations.
- Run the existing report download/share browser coverage; add interaction tests only if UI behaviour changes. Run the backend suite and production build before PR merge, then verify the deployed release.

## Acceptance

The resulting report follows the supplied form's structured property/summary/activity/notes layout, uses the existing brand, and preserves the established reporting measurements and evidence. Ratings and invoice fields are absent. Report creation and sharing remain tenant-admin-only.
