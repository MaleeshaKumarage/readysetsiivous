# Shift photos, notes and report appendices

Issue102 adds saved shift evidence to monthly shift and branch PDFs downloaded by
tenant admins and newly created public report links. Summary tables appear first; an appendix follows for each submitted
record containing photos or a nonblank note. Company, branch, shift and the
scheduled shift date/time in the tenant timezone identify each entry.

Each photo has its own appendix page with its individual photo note and optional
general shift note beside it.
Portrait and landscape photos retain their aspect ratio. Notes alone get an
appendix entry without an image. Long notes can span pages, with shift context
repeated in the header. No appendix is created when there is no submitted evidence.
Draft/unsubmitted records are excluded. Historical submitted evidence remains
available under the report's existing branch/shift history rules.

## Submission contract

- Every new photo requires its own nonblank note, submitted as `photos: [{url, note}]`.
  A general shift note cannot substitute for individual photo notes. Legacy
  nonempty `photoUrls` uploads, including ambiguous mixed payloads, are rejected.
- General shift notes are optional with or without photos, and may be submitted alone. Checklist
  selections remain measurements: none, some or all may be selected.
- Both authenticated end-shift and legacy token submissions enforce the note rule
  before mutating activities, submission state or timestamps. A rejected API call
  returns400 with a `Photos` validation error and saves nothing.
- Removing a photo removes its own note, preserving other image/note pairs. The UIs retain photos,
  notes and selections after failed saves and wait for photo reading to finish.
  Reader errors permit retry or note-only completion.
- The file pickers accept PNG, JPEG, WebP and BMP up to10 MiB per file. Evidence
  still uses legacy inline data URLs; this is not a new private upload/storage API.
  Broader server-side upload limits, review UI and storage policy remain issue77.

## Historical images and sharing

The renderer embeds saved raster data URLs. It does not fetch remote URLs or local
file paths from stored photo strings. Corrupt, unsupported or oversized historical
images show a visible unavailable message while retaining the date and note.
Decode limits are10 MiB,40 million pixels and16,384 pixels on either axis.
Existing submitted photos without notes are retained with a historical missing-note
message; new submissions must satisfy the note requirement.

The user approved evidence in newly created public report links. Snapshots retain
submitted photo notes and general notes through the safe evidence projection,
while clearing full forms and their cleaner names, IDs and checklist tokens.
Anyone with the link can view the appendix. Tenant admins alone create/revoke links;
30-day expiry and tenant boundaries remain enforced. Old summary-only snapshots
remain summary-only; historical URL-only images retain their old general note,
without invented individual captions. The additive `Photos` field needs no SQL
migration and absent fields in saved documents/snapshots default to empty.
New forms and shared snapshots store each image once in the paired `Photos` field;
form DTOs derive the historical `photoUrls` read view for compatibility.

## Verification mapping

`QualityCyclePhotoReportTests` covers atomic domain/handler validation, actual HTTP
400 and note-only recovery, submitted-only projection, tenant-local month windows,
share evidence retention and identity/token redaction, real portrait/landscape/JPEG/WebP PDF images, note-only
pages, long notes, older snapshots and malformed/oversized/remote image fallback.
The sample PDF was rendered and visually inspected; text/resource inspection
confirmed the tenant-local shift dates, repeated notes and embedded image objects.

`QualityCyclePhotoNotesTests` adds multiple distinct notes, atomic per-photo validation,
URL-only/ambiguous request rejection and immutable old summary-only snapshots.
`my-shifts.spec.ts` and `quality-cycle.spec.ts` cover desktop/mobile upload,
blank/whitespace note rejection before a request, save retry retention, removal,
standalone notes and photo-read timing/error. Existing permission, expiry,
revocation and optional checklist regressions remain in the QC suite.
