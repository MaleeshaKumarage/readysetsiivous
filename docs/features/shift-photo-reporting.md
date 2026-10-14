# Shift photos, notes and report appendices

Issue102 adds saved shift evidence to tenant-admin downloaded monthly shift and
branch PDFs. Summary tables appear first; an appendix follows for each submitted
record containing photos or a nonblank note. Company, branch, shift and the
scheduled shift date/time in the tenant timezone identify each entry.

Each photo has its own appendix page with the shift note repeated beside it.
Portrait and landscape photos retain their aspect ratio. Notes alone get an
appendix entry without an image. Long notes can span pages, with shift context
repeated in the header. No appendix is created when there is no submitted evidence.
Draft/unsubmitted records are excluded. Historical submitted evidence remains
available under the report's existing branch/shift history rules.

## Submission contract

- A nonblank shift-level note is required whenever at least one photo is attached.
  That note describes all photos in the submission; separate photo captions are
  not part of the current data contract.
- Notes are optional without photos, and notes may be submitted alone. Checklist
  selections remain measurements: none, some or all may be selected.
- Both authenticated end-shift and legacy token submissions enforce the note rule
  before mutating activities, submission state or timestamps. A rejected API call
  returns400 with a `CleanerNotes` validation error and saves nothing.
- Removing the last photo removes the note requirement. The UIs retain photos,
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

Current public report links remain summary-only: snapshot creation clears both
private forms and the report evidence projection. Cleaner names, IDs, tokens,
notes and photos are not newly published. Existing links remain frozen, expire
after30 days, and can be revoked by tenant admins. Including evidence in new
public links requires a separate explicit user decision.

## Verification mapping

`QualityCyclePhotoReportTests` covers atomic domain/handler validation, actual HTTP
400 and note-only recovery, submitted-only projection, tenant-local month windows,
share snapshot redaction, real portrait/landscape/JPEG/WebP PDF images, note-only
pages, long notes, older snapshots and malformed/oversized/remote image fallback.
The sample PDF was rendered and visually inspected; text/resource inspection
confirmed the tenant-local shift dates, repeated notes and embedded image objects.

`my-shifts.spec.ts` and `quality-cycle.spec.ts` cover desktop/mobile upload,
blank/whitespace note rejection before a request, save retry retention, removal,
standalone notes and photo-read timing/error. Existing permission, expiry,
revocation and optional checklist regressions remain in the QC suite.
