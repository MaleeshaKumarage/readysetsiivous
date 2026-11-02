# Field-service workflow delivery ledger

Approved design: [design](../superpowers/specs/2026-11-02-field-service-workflows-design.md).
Reusable implementation checklist: [plan](../superpowers/plans/2026-11-02-field-service-workflows-plan.md).

## Current checkpoint

- Baseline: main `b4f7b6260797b3784c0b3de295464d1373fee5fd`.
- Implementation branch: `feat/field-service-workflows`.
- Scope authorised: approved six-stream plan, delivered as **small sequential PRs**, focused Playwright tests in each, review/fix/merge, then saved handoff and context compaction before next chunk.
- First chunk: C01 — server-search company selectors in Agreements/Shifts, preserving selected context and removing the first100 cutoff.
- C01 implemented and verified locally: shared paged server-search company selector in Agreements/Shifts; selected context survives searches and page changes, stale responses are ignored, and shift branches reset when the company changes. PR review, merge and deployment remain separate checkpoints.
- Autoaccept A01 is the next separate chunk after C01. User confirmed exact admin-planned start is required. Employee-owned preference defaults Manual; hide approvals only after confirmed enabling, which is blocked while any pending offers remain. Disabling is future-only. Use atomic Offer/acceptance and the same preference/work-reservation locking; notifications are informational, not approval prompts.

## Per-PR ledger

| Chunk | Branch / PR | Implemented | Verified | Merged | Deployed | Handoff |
| --- | --- | --- | --- | --- | --- | --- |
| C01 | feat/field-service-workflows / PR pending | yes | yes (local checks below) | no | no | checkpoint below |

## Handoff template

Append one checkpoint after every reviewed chunk, before compacting context:

- Date; chunk IDs and exact shipped behaviour.
- Branch, commit SHA, PR URL and reviewed head.
- Files/interfaces changed; compatibility/permission notes.
- Commands and test evidence; failures or limitations.
- Review findings and disposition.
- Merge and deployment status (separate; verify live SHA before claiming deployed).
- Outstanding decisions and next dependency-ready chunk.
- Unrelated working-tree files preserved; any active local processes or special test prerequisites.

Never store credentials, customer personal data, production exports or private host configuration here. Machine-local operations references stay in the ignored runbook.

### C01 implementation checkpoint — 2026-11-02

- Implemented the shared bounded CompanyPicker for Agreements and Shifts, including server search, paging beyond the former first-100 cutoff, selected-company hydration, inactive company labels, failure/retry handling and stale-response protection. Shift branch loading is guarded against stale company responses and resets dependent branch selection.
- Added desktop/mobile picker E2E coverage and updated existing admin tests for the searchable selector. Persisted the approved six-stream design and implementation plan; later chunks remain planned.
- Verification passed: 12 new picker desktop/mobile E2E cases (53.2 seconds); 34 existing lifecycle-agreement-email, lifecycle-agreements-employees, lifecycle-shifts, shift-dates and company-integrity cases; 10 admin Agreement/Shift cases; `npx tsc --noEmit --incremental false`; `npm run build` (47 pages).
- E2E commands (from `site`, both default desktop/mobile projects): `npx playwright test lifecycle-company-picker.spec.ts --workers=1`; `npx playwright test lifecycle-agreement-email.spec.ts lifecycle-agreements-employees.spec.ts lifecycle-shifts.spec.ts shift-dates.spec.ts company-integrity.spec.ts --workers=1`; `npx playwright test admin.spec.ts --grep 'Admin Panel - (Agreements|Shifts) Page' --workers=1`.
- Independent code review reported no blockers; specification/documentation review passed. No backend behavior changed in C01.
- Commit and PR references are pending publication. Merge and deployment have not been performed. Unrelated untracked workspace files are preserved.
- Next dependency-ready chunk: A01, with exact admin-planned start required and enabling autoaccept blocked while any pending offers remain; retain the atomic Offer/acceptance and shared work-reservation locking boundaries below.
### Next planned chunk

A01 (employee-owned work autoaccept) follows C01. Start-time question resolved: user confirmed an admin must set the exact planned start. Enabling is blocked while any existing pending offers remain, with an explanation to resolve them first; no silent hiding. Disabling affects future offers only. Use self-owned work-preference endpoints, one atomic server Offer/acceptance commit and the same work-reservation locking for Offer and preference changes. Only informational notifications are sent, not approval-prompt email. These boundaries are authorised for implementation. This documentation update does not implement app behaviour.
