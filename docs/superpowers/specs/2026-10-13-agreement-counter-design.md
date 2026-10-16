# Agreement detail/create counters — issue69

Status: proposed; current source inspected at `612264c`/`d19b947`.
Independent of employee role decisions and service edits.

## Confirmed defect

AgreementDetailDto lacks SignerCount/SignedCount. Frontend AgreementDetail extends
AgreementListItem and the modal renders both fields. Create and get both construct
AgreementDetailDto.From, so actual JSON produces undefined counters; list-shaped
mock detail payloads currently hide this mismatch.

## Options

1. **Recommended:** add computed counts to the detail DTO so existing list/detail
   frontend contracts agree. Derive total from Signers.Count and signed from exact
   Signed status. This fixes both create and get without redundant stored counters.
2. Change the frontend detail interface and derive counts there. Valid but leaves
   list/detail wire shapes different and spreads count semantics into consumers.
3. Persist count columns. Unnecessary schema/state synchronization for derived data.

## Proposed behavior

- Add computed `signerCount` and `signedCount` JSON properties to AgreementDetailDto.
  Do not alter constructor callers, existing signer fields or persistence schemas.
  Existing frontend AgreementDetail extends the now-accurate list contract.
- Count every signer row for the total; only exact domain Signed status contributes
  to signed count. Unknown or pending statuses never count as signed. Zero signers
  reports0/0. Count independently of agreement lifecycle status/active flag.
- Create and get JSON reflect their current signer snapshot; after signing/add/
  remove and re-fetching, counters match the current records. Do not infer signed
  count from Completed/PartiallySigned status or keep a mutable duplicate counter.
- Make mocks faithful to the actual DTO. Add fixtures built from signer states,
  with create/get/list readbacks updated after mutations. Ensure tests originally
  fail against the missing backend fields; do not manufacture them solely in mocks.
- Keep existing signing capability, lifecycle and admin permissions unchanged;
  employee-sensitive read restrictions remain issue65. This fix cannot certify
  authorization or new cancel/activate policy.

## Tests and acceptance

- Unit: actual serialized detail DTO at0/0,0/N,1/N,N/N, pending/unknown statuses,
  inactive/cancelled/completed agreement snapshots; exact camelCase fields.
- Handler/contract tests: create result and detail handler result use the same
  counters; list counters agree with detail counters for the same snapshot.
- Real PostgreSQL repository readback through handlers after signer changes:
  pending→signed, partial→all, add/remove; unrelated agreement/company unaffected.
  Use isolated owned records. A source-only DTO test is not persisted proof.
- Playwright desktop/mobile: modal renders numeric counts for actual detail/create
  shape,0/0 and partial/all states, and updated count after refetch; no undefined
  labels or list-shaped stale mocks. Genuine dev/isolated API readback verifies
  actual returned JSON includes counters after signatures.
- Full backend/browser regression, type check/build. Traceability:
  AGR-001/006/011. Record exact evidence and limitations before issue completion.

Acceptance is correct counters from actual create/get wire payloads and subsequent
signature-state readbacks, with contract-faithful browser fixtures and persisted
integration evidence. Broader agreement lifecycle changes remain issue73.
