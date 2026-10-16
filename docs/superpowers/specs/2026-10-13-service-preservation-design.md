# Preserve service data during edits and status changes — issue68

Status: proposed; current source inspected at `612264c`/`d19b947`.
This is independent of the pending employee/manager scope decision.

## Confirmed defect

ServicesAdminPage sends a full replacement containing only Finnish translations,
hardcoded icon/currency/order/featured and null additional information; image URL
is omitted. Status changes omit image/information too. The backend Apply method
replaces all these values. `Number(vat) || 25.5` replaces a valid zero rate.
The service list client type also omits currency/version, hiding preservation and
concurrent-update requirements. No implementation is claimed here.

## Options

1. **Recommended:** narrow admin editor/status commands merge only intended fields
   into the current service, with an expected version. Preserve the existing full
   create/update APIs for their current clients. This closes unrelated-field loss
   and rejects stale UI saves instead of silently overwriting another edit/upload.
2. Send the complete original frontend snapshot through existing PUT. Lower API
   change, but retains stale-snapshot metadata loss and requires every future
   persistence field to be maintained in frontend replacement code.
3. Build a complete multilingual/metadata editor. Useful later, but a larger UI
   feature than correcting destructive edits and zero-VAT behavior.

## Proposed behavior

- Add admin-only `PATCH /api/v1/admin/services/{id}` for explicitly changed editor
  fields and `PATCH /api/v1/admin/services/{id}/status` for active status only.
  Both require the expected Marten version from the list snapshot. Missing/invalid
  version is400, stale/concurrent version409, foreign/missing ID404. No new roles.
- Editor allowlist: slug, Finnish name/description, net price, VAT percent and
  duration. Do not send hidden category or untranslated metadata. Only dirty fields
  are sent. Description empty string deliberately clears the Finnish description;
  omitted/null optional fields mean unchanged. Finnish name must be nonempty when
  changed; do not copy an English fallback into Finnish while editing another field.
- Server applies changed Finnish values while retaining every other translation
  key and preserves icon, currency, order, featured, image and additionalInfo. It
  cannot change active status through the editor command. Status command changes
  only IsActive plus normal modification/version metadata.
- Read current service, verify expected version before mutation, then save with
  existing optimistic concurrency. A concurrent write between load/save also409.
  Do not silently merge conflicting versions or retry a stale update.
- Existing full replacement PUT remains a replacement contract for existing clients;
  the UI switches to narrow commands. New clients should use them for these intents.
  No database migration is needed; Marten already hydrates the version on read.
- Use an explicit shared frontend service row type including currency/version,
  image and additional information. Current Finnish editor controls remain; display
  the actual ISO currency and identify the editable price as net. Public price
  basis/VAT policy remains the separate issue84 decision.
- Creation retains explicit defaults for blank numeric fields: net0, VAT25.5,
  duration60. Entered zero remains zero. Editing blank numerics is a visible error,
  not an implicit reset/default. Reject nonfinite/negative amounts, VAT outside
  0..50 and noninteger duration outside15..1440. Match server validation rather
  than silently clamping user inputs.
- Track per-row status mutation and modal save; reject repeated clicks while busy.
  Show network/400/409/other failure visibly and always clear loading in finally.
  On conflict, preserve the draft, explain reload is required, and offer a reload
  that does not silently submit the stale draft. Refresh list after successful writes.
  A no-change edit cannot trigger a destructive replacement.

## Test requirements

- Backend unit tests: one-field change, mixed dirty fields, fi/en/sv plus extra
  locale preservation, metadata preservation, empty/omitted Finnish description,
  empty Finnish name rejection, status-only transition, missing/foreign ID,
  stale/missing version, no-write on failures and all numeric boundaries.
- PostgreSQL: persisted readback after edit and activate/deactivate, current
  versions, stale editor/status/upload snapshot conflict and simultaneous writes.
  Expected assertions must compare independently seeded sentinel translations,
  currency/icon/image/info/order/featured values, not only the request payload.
- Playwright desktop/mobile: edit populated service, actual narrow request/dirty
  fields, stateful readback, status preservation, zero-VAT creation/edit, invalid
  numeric errors, preserved draft on409, network retry and duplicate clicks.
- Genuine admin/API browser test against owned dev/isolation: create seed through
  full API, use UI edit/status commands, reload persisted service and public locales
  to confirm untouched content. Never use a permanent success mock for readback.
- Full backend/browser regression, type check/build. Traceability:
  SVC-001/004/005 and CAT-005. No change to public price policy or broad auth scope.

## Acceptance

The fix is complete only when editing one visible field and toggling status retain
all unrelated persisted fields, zero VAT survives readback and stale writes cannot
erase newer state. Document exact tests/results and release prerequisites in the
feature contract and issue68; do not claim a multilingual editor was added.
