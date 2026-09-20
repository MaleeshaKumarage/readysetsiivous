# Agreement E-Signature — Design

Date: 2026-09-20
Status: Approved for implementation planning

## Goal

Let an admin upload a PDF agreement, define the list of signers, copy each signer a
personal link, and have signers draw or type their signature on the last page via a
public web page. When every signer has signed, the final PDF — original document with a
signature block stamped on the bottom of the last page — becomes downloadable from the
same link.

## Decisions

1. **Simple image signature** — drawn/typed image stamped onto the PDF. Not legally
   eID-binding (no strong identity verification). Suitable for quotes, order
   confirmations, service agreements; not a substitute for bank-ID contracts.
2. **Parallel signing** — signers sign in any order; agreement completes only when all
   have signed.
3. **Fixed signature position** — a signature block on the bottom of the last page. The
   admin guarantees the source PDF leaves blank space there. No signer-chosen position.
4. **Predefined signer list** — admin adds each signer (name + email); each gets a
   personal high-entropy link. Completion = every listed signer signed.
5. **Manual link delivery** — the app generates the signer link and the admin copies it
   into their own email client. No SMTP integration.
6. **Rasterize the final PDF** — all pages rendered to images, signature block drawn on
   the last page, PDF rebuilt from images. Final PDF text is not selectable. Uses MIT
   libraries; no paid/AGPL dependency.

## Architecture

### Data model (Marten, per-tenant)

`Agreement` (inherits `BaseDocument`, deterministic id via `TenantDocumentIds`):
- `Slug` — tenant slug.
- `Title` — document name.
- `OriginalPdfPath` — stored uploaded PDF.
- `Status` — `Draft` | `PartiallySigned` | `Completed` | `Cancelled`.
- `Signers` — list of `Signer`.
- `SignedPdfPath` — final stamped PDF (set on completion).
- `CreatedUtc` / `UpdatedUtc` / `CompletedUtc`.

`Signer` (value object inside Agreement):
- `Id` (Guid), `Name`, `Email`.
- `Token` — 32 random bytes, base64url, per-signer secret.
- `Status` — `Pending` | `Signed`.
- `SignatureImagePath` — PNG of the drawn/typed signature.
- `TypedName` — the name the signer entered at sign time.
- `SignedAtUtc`.

### Flow

1. Admin creates an agreement: title + uploads the PDF.
2. Admin adds signers (name + email). Each signer gets a `Token`.
3. Admin copies each signer's link: `/{lang}/sign/{token}`.
4. Signer opens the public page (no login), previews the PDF, draws a signature on a
   canvas or types their name (rendered in a cursive font), enters their full name, and
   presses **Sign**.
5. Signature captured as a PNG, stored; signer marked `Signed`.
6. When all signers are `Signed`, status flips to `Completed`, the final PDF is generated,
   and it becomes downloadable from every signer link and from the admin detail page.

### Signature capture

- **Draw:** HTML `<canvas>` (mouse/touch). Serialized to a transparent-background PNG
  (base64).
- **Type:** the typed name rendered on the same canvas using a bundled script/cursive web
  font, exported as PNG. A toggle switches between draw and type.
- The PNG is posted to the backend and saved under the agreement's storage dir.

### PDF stamping (last page bottom)

Pipeline, run once when the last signer signs:

1. Render every page of the original PDF to an image — `PDFtoImage` (MIT, Pdfium-based).
2. On the last page's image, draw the signature block with SkiaSharp (already available
   transitively via QuestPDF):
   - Each signer gets a slot: typed name, `SignedAtUtc` (Europe/Helsinki), signature
     image.
   - Slots laid out left-to-right, wrapping to additional rows within the reserved blank
     bottom area.
3. Rebuild the final PDF from the page images with QuestPDF (one full-bleed image per
   page).
4. Store to `SignedPdfPath`.

### Storage

Local disk, reusing the existing `Uploads:Path` convention:
`uploads/agreements/{agreementId}/original.pdf`, `/{signerId}.png`, `/signed.pdf`.

### Endpoints

Public (token-gated, no auth):
- `GET  /api/v1/public/{slug}/agreements/{token}` — agreement + signer + original PDF for
  preview (status-aware).
- `POST /api/v1/public/{slug}/agreements/{token}/sign` — body `{ typedName, signaturePng }`.
- `GET  /api/v1/public/{slug}/agreements/{token}/document` — final PDF (only when
  `Completed`).

Admin (`[Authorize admin]`):
- `POST   /api/v1/admin/agreements` — create (multipart PDF upload).
- `GET    /api/v1/admin/agreements` — list.
- `GET    /api/v1/admin/agreements/{id}` — detail incl. signer tokens + status.
- `POST   /api/v1/admin/agreements/{id}/signers` — add signer.
- `DELETE /api/v1/admin/agreements/{id}/signers/{signerId}` — remove pending signer.
- `GET    /api/v1/admin/agreements/{id}/document` — download final (or original) PDF.
- `POST   /api/v1/admin/agreements/{id}/cancel` — cancel.

### Admin UI (shadcn)

New page `src/app/[lang]/admin/agreements/`:
- List view (Table) of agreements with status Badge.
- Create Dialog: title field, PDF file input, repeatable signer rows (name + email).
- Detail view: per-signer status, copy-link button (navigator.clipboard), download.

New admin route entry on the existing dashboard (`admin/page.tsx` SECTIONS array).

### Sign page (public)

New page `src/app/[lang]/sign/[token]/page.tsx`:
- Native `<iframe>`/`<object>` PDF preview served from the backend.
- Canvas signature pad + draw/type toggle.
- Name input.
- Sign button (posts name + PNG).
- Download button shown when the agreement is `Completed`.
- Language-aware via existing i18n.

### Security

- Token = 32 cryptographically random bytes, base64url; the URL is the bearer credential.
- Tokens are single-purpose (per signer) and immutable once generated.
- Signature PNGs validated for size/type (PNG, capped dimensions).
- Rate-limit `sign` and `document` endpoints.
- Final PDF immutable once `Completed`; cancellation only from `Draft`/
  `PartiallySigned`.

### Error handling

- Signing an already-signed or unknown token → RFC 7807 (404/409).
- Signing a cancelled agreement → 409.
- Final-PDF generation failure → 500 problem detail; agreement stays `PartiallySigned`
  and generation can be retried (idempotent: regenerate on a dedicated admin retry or
  lazily on `document` fetch if `Completed` but file missing).

### Testing

- Backend: agreement create/list, signer add/remove, sign flow (single + all-parties →
  completion), token auth failure cases, stamping produces a valid multi-page PDF with
  the expected page count, final PDF served only when `Completed`.
- Frontend: sign page renders, draw/type toggle produces a PNG, sign posts correctly,
  download appears on completion.

## Out of scope

- Strong eID / legally binding signatures.
- SMTP email sending (admin copies links manually).
- Document templates or automatic data merge into the PDF.
- Signer-chosen signature positions.
