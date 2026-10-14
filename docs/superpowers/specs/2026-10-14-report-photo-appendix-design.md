# Shift report photo and note appendix

The user requires uploaded shift images in the final report PDF, with shift dates
and notes in an appendix. Images require a nonblank note; a note may be recorded
without an image. Checklist selections remain optional measurements.

## Scope and existing behavior

The active monthly PDF generator renders tables only, despite receiving forms
containing inline FileReader image data URLs and cleaner notes. The legacy summary
generator prints only a photo count. Existing public report snapshots intentionally
remove private forms, names, notes and photos.

Implement the requested appendix in tenant-admin downloaded monthly PDFs (shift
and branch scopes use the same generator). Retain the current public snapshot
privacy contract unless the user explicitly elects to publish the appendix through
new report links. Previously created links remain frozen; expiry and revocation
remain unchanged. This scope does not introduce an upload storage service or
arbitrary remote image fetching.

Use the existing shift-level note for all its photos, consistent with the current
UI and persisted form contract. Separate per-photo captions would require a
different submission/storage contract and will apply only if explicitly requested.

## Data and validation

Add a report-only evidence projection containing occurrence start/end UTC, note
and photo data URLs, without cleaner identifiers, names or form tokens. Each
submitted form with photos or a nonblank note contributes one record. Exclude
unsubmitted records and retain the report's existing company/branch/shift and
month grouping. Sort evidence by occurrence time; keep separate cleaner
submissions separate, without introducing identities into the appendix.

The evidence collection has an empty default to remain compatible with older
serialized reports. For existing stored forms, the report generator may project
evidence from forms when the new collection is absent/empty.

In domain submission, normalize photo entries and trim the note before mutation.
Reject any nonempty normalized photo collection with a null/empty/whitespace note.
Run this validation before updating checklist selections or submission state.
Both authenticated end-shift and legacy token submission use that domain rule;
the application maps its argument error to a 400 validation response. Notes alone
and no photos/no notes remain valid, including none/some/all checklist selections.
Existing submitted evidence is not retroactively invalidated.

## PDF layout and image handling

Render the summary pages first, followed by an appendix for all evidence-bearing
shifts. Repeat company/branch/shift context and the scheduled shift's tenant-local
date/start time on each evidence page. Add notes as normal flow text so long notes
can paginate without clipping. Photos follow their record's note and keep original
aspect ratio inside a bounded page area. Show a note-only entry when no photos
exist; omit the appendix entirely when there is no evidence.

Decode supported embedded raster images (PNG/JPEG/WebP/BMP) from base64 data URLs
only. Use the existing QuestPDF image renderer. Reject oversized encoded data
before allocating decode buffers; a maximum of 10 MiB per legacy image matches
the current public UI's advertised limit. Malformed, unsupported, missing or remote
legacy image references produce a visible image-unavailable entry with the date
and note retained; they do not fail the whole report. Never fetch a remote URL or
local file path from a stored photo string, and never print raw data URLs or tokens.
Large-image ingestion/storage limits remain part of the broader issue77 work.

The legacy generator also gets an appendix, using UTC since it has no tenant
timezone parameter. It should no longer imply that a photo count means images
were embedded.

## Frontend behavior

Both shift completion and legacy token form label the note as required when photos
are present. Before sending, reject blank notes with an accessible inline message
and focus the note field. Keep all photo previews and checklist selections for
retry. Removing the last photo removes the note requirement. Keep notes editable
without photos. A FileReader-in-progress state prevents completion from racing
ahead of an image upload; reader errors keep the form usable with a clear message.

## Verification and documentation

Domain/application cases: photo+blank note rejection without mutation; note-only
and empty submissions accepted; trimmed note; authenticated and token endpoints
share the rule; ownership and future/already-submitted guards unchanged.

Report cases: submitted images/notes included; drafts excluded; note-only entries;
company/branch/shift and month separation; tenant-local dates including UTC date
rollover; two cleaners' evidence; older snapshot defaults; shared report privacy,
30-day expiry and revocation unchanged unless the user authorizes sharing changes.

PDF cases: real image resources and rendered pages, multiple photos, portrait and
landscape photos, long notes/multipage output, no-evidence output, and corrupt or
remote historical references with visible fallback. Visually render sample PDFs
and inspect all pages. HTTP tests verify admin-only exports remain enforced.

Playwright: upload+blank/whitespace note blocked before request, successful upload
and note, notes without image, removal of the last photo, retry retains all input,
and upload timing/error. Cover desktop/mobile and legacy submission as applicable.
Update feature documentation and the edge-case catalogue. Push a reviewed PR from
latest main, pass appropriate CI, merge and report deployment status accurately.
