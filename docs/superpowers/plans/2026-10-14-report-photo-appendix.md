# Report photo appendix implementation

1. Enforce the photo/nonblank-note rule before domain mutation; map its error to
   the note field for both authenticated completion and legacy token submissions.
2. Add submitted-only report evidence with occurrence dates, notes and photos;
   explicitly redact it together with Forms from existing public share snapshots.
3. Render end-of-document appendices for active monthly and legacy summary PDFs.
   Bound inline image decoding with codec metadata; show visible fallback for
   unsupported/corrupt/oversized/remote legacy images. Preserve date/notes and
   paginate long content and multiple images without clipping.
4. Add conditional note validation, accessible messages and pending/error photo
   reads to both submission UIs. Preserve all input on failures and allow removal
   and standalone notes.
5. Test real PDF images and rendered page layout, domain/handler atomic rejection,
   monthly filtering and share privacy, and desktop/mobile upload flows. Update
   feature/edge-case docs, push PR, resolve review findings, pass CI and merge.

The brainstorming spec reviewer approved the written design after dimensions and
share-redaction checks were added. The writing-plans skill is not available in
this session, so this plan follows repository document conventions directly.
Optional public-evidence/per-photo-caption choices can amend the approved scope
when the user responds; do not publish private evidence by default.
