# Per-photo notes and public report appendix

The user approved the same photo/note appendix in newly created public report
links and requires a separate note for each photo. Links expire after 30 days and
remain revocable by tenant admins. Existing link snapshots must not change.

Add typed photo evidence objects (url, note) to shift task submissions, persisted
forms, DTOs and the safe report evidence projection. Keep historical PhotoUrls and
CleanerNotes readable; new clients submit Photos rather than unpaired URL strings.
For new submissions every photo requires a nonblank URL and its own nonblank note,
validated atomically before checklist/completion mutation. Legacy URL-only uploads
are rejected with an actionable validation error; a general shift note cannot
substitute for per-photo notes. Reject ambiguous nonempty new Photos plus legacy
PhotoUrls. No-photo submissions still allow optional standalone shift notes.

Cleaner start/end and legacy token form UI show a required note field next to every
preview. Keep photo and note together during upload/removal, preserve drafts after
failed saves, focus the first missing photo note and block submission until all
photo notes are filled. General shift notes remain optional. Read failures/pending
uploads retain their existing guards. Removing one photo removes only its own note.

PDF appendix pages show company/branch/shift, tenant-local occurrence date/time,
the matching photo note and optional general shift note. Historical URL-only
evidence retains its original general note or the existing missing-note message;
do not invent photo-specific historical annotations. Existing raster decode limits,
unavailable-image placeholders and no remote fetching remain unchanged.

New public snapshots retain the safe evidence projection but clear full Forms,
which contain employee identifiers/names and form tokens. Include submitted-only
evidence, never drafts, in both admin and public PDFs. Public downloads render only
the frozen snapshot; expiry/revocation and tenant boundaries remain enforced.
Explain in admin share UI that photos and notes are included for anyone with the
link. Existing summary-only snapshots remain summary-only.

Tests cover missing/whitespace photo notes even with a general note, multiple
distinct notes, removal association, read/save failures, standalone notes, atomic
validation, legacy history rendering, same admin/public appendix content without
identity/token fields, frozen old/new snapshots, expiry/revocation/tenant access.
Render and inspect a multi-photo PDF with different notes. No role/SQL schema
change; document the additive JSON fields and stricter new-upload contract.
