# Open PR review and consolidation

Reviewed the nine open ReadySetSiivous PRs against current main. The older
`cleaning-suite-api` repository had no open PRs. Useful changes were consolidated
into two PRs, rebased and verified before merging.

| Original PR | Decision | Retained changes |
| --- | --- | --- |
| #29 | Superseded by merged #61 | Menu focus ring; existing localized open/close labels preserved |
| #35 | Superseded by merged #61 | Explicit localized keyboard-accessible WhatsApp hint dismiss button |
| #42 | Superseded by merged #61 | FAQ and WhatsApp focus states; explicit dismiss button chosen over making the whole hint a button |
| #60 | Superseded by merged #61 | Required/invalid form states, associated errors and submission busy state |
| #61 | Merged as `8dc7a4d` | Consolidated public-site accessibility improvements |
| #30 | Superseded by merged #36 | Timezone lookup cache and hoisted slot calculations |
| #41 | Superseded by merged #36 | Faster overlap detection; unsorted inputs retain pairwise behavior |
| #59 | Superseded by merged #36 | Sorted overlap scan and larger-list regression coverage |
| #36 | Merged as `3ce2692` | Consolidated availability and shift-overlap optimizations |

Excluded stale conflict-resolution changes to shift unassignment and database
startup from #29. Excluded unrelated changes to swallow additional live Gemini
test failures from #59 and obsolete agent journal files. Current booking notice
periods, inactive-service checks, business windows, tenant timezones and touching
interval behavior remain intact. There are no schema, role or API-contract changes.

The overlap scan uses the fast path only for sorted positive intervals. Point,
reversed and unsorted intervals use the original pairwise predicate. This avoids
the zero-length interval false negative found during review.

Verification:

- #61: full CI and automated review passed; all 184 desktop/mobile cases passed.
  Sixteen new focused cases also passed locally, covering three languages,
  keyboard focus/dismissal, all six FAQ answers and quote validation/submission.
- #36: rebased onto the merged #61 main before final CI. Automated review and
  full CI passed: 377 backend tests passed, five existing fixture-dependent
  tests skipped, and all 184 desktop/mobile cases passed.
- All 39 focused availability/shift tests passed locally without skips, including
  business-window durations, timezone transition dates, repeated lookups,
  touching/nested/unsorted/degenerate intervals and 1,000 deterministic
  comparisons against the original pairwise overlap oracle.

CI evidence: [accessibility](https://github.com/MaleeshaKumarage/readysetsiivous/actions/runs/38488965253),
[final rebased scheduling](https://github.com/MaleeshaKumarage/readysetsiivous/actions/runs/38489463189).
Merge verification does not establish production deployment of these new
optimizations; deployment is tracked separately by the existing workflows.
