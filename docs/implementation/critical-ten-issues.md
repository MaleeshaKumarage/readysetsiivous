# Critical issue batch — 2026-11-09

The user requested one PR for ten issues. Selection prioritizes remaining reproducible security, lost-write,
operational and verification failures over roadmap features, policy decisions, operator-only repairs and
work already covered by another open PR. The one-PR request overrides the normal small-PR convention.

| Issue | Change in this batch | Regression evidence |
| --- | --- | --- |
| #126 | Keep game action quotas with the authenticated player across reconnects and tab replacement. | GameSessionManagerTests; existing real SignalR identity/takeover tests. |
| #127 | Once an answer is submitted, retries must use that answer even after provider failure; changed retries cannot invoke the provider or alter the pending answer. | EntertainmentHubIntegrationTests with real SignalR transport. |
| #79 | Optional 256-bit booking request credential, deterministic tenant-scoped identity and durable payload digest; replay returns the original confirmation and management key, while changed payloads conflict. The form retains an uncertain request in memory and offers explicit recovery. | Handler tests, PostgreSQL concurrent retries/new-session readback, desktop/mobile lost-response test. |
| #320 | Test-only Marten stores drop their own GUID-suffixed fixture schema on synchronous or asynchronous disposal. Database fixtures run in an isolated collection; individual race tests remain concurrent. | Real PostgreSQL sync/async disposal test; full backend suite. |
| #285 | Unstarted visits whose window ended move to Needs action. Its default filters include assigned overdue work from the existing 180-day history window. Past unfinished one-time definitions ask the admin to check completion. | Queue rules and real query tests; desktop/mobile default filters and list status. |
| #333 | Conflict details include recurrence and open-ended status, with cleaner/date links to the employee schedule. Deep links filter the cleaner and select the clashing week. | Assignment refusal tests; desktop/mobile overlap and schedule deep links. |
| #295 | Inactive shifts' unstarted future queue entries are Cancelled; submitted/started evidence is retained. The generated queue horizon is capped at 30 days ahead. | Real PostgreSQL query with inactive, open-ended and overdue shifts. |
| #296 | Require a deliberate schedule choice and allow new On-call / flexible definitions. Existing dated-visit scheduling follows creation; no invented daily visits. Daily/weekly labels use HH:mm; biweekly and monthly labels include their saved times. | Desktop/mobile empty schedule and on-call creation; updated create-flow regression specs. |
| #292 | Shared bounded access verification distinguishes network failure, HTTP server failure and 401/403 denial, with safe support references and retry. | Desktop/mobile admin and manager negatives/recovery. |
| #317 | Missing partner body errors identify the four required fields; field conversion errors keep their specific safe field message. | Model-state regressions and existing real service validation test. |

## Boundaries

This batch does not perform deployments, edit production data, merge another PR or rewrite financial snapshots.
Booking recovery credentials and customer drafts remain in active page memory only; refreshing discards them.
The optional request field preserves older clients. Existing booking documents require no backfill or new index.
Game quotas remain process-local; the existing room retention bounds their lifetime. Queue generation remains a
bounded projection rather than a durable cancellation of the original schedule.

The older security/game/booking trackers contain wider historical acceptance lists. Their existing implementation
and this batch's regressions must be considered together; this document does not claim a new live Keycloak or
multi-replica verification run. Issue closure is left for review rather than automatically closing broad trackers.

## Local verification

- Full backend suite against isolated PostgreSQL 15.18: 1,616 passed, 2 external Keycloak fixtures skipped.
- After the full run, the final two shift-tab provider lifetimes were corrected; the three affected
  storage/cleanup regressions passed and a catalog query confirmed zero remaining fixture schemas.
- Final workflow browser run: 231 of 232 desktop/mobile cases passed. The manager navigation case
  was synchronized with destination-route completion and then passed on both projects (2/2).
  The other 34 manager cases also passed in the scoped manager run. All 232 selected cases have
  successful verification; these counts describe multiple runs, not one uninterrupted green run.
- TypeScript passed; frontend unit tests: 2 passed. Production build passed and exported 53 static pages.

CI uses PostgreSQL 16; its latest-head checks and review remain required before merge. Implementation,
publication, merge and deployment remain separate states. Review branch: `fix/critical-ten-issues`.

## Main integration

PR [#357](https://github.com/MaleeshaKumarage/readysetsiivous/pull/357) integrates main `4a3c20f`
(PR #340), which merged during this task. Conflict resolutions preserve both named overlaps and the
recurrence metadata, the shared inactive-work rule and started/submitted evidence, upstream schedule
formatting and biweekly times, and the updated empty-Today copy with synchronized manager navigation.

Post-integration verification: 70 backend checks passed with zero fixture schemas remaining. The 114-case
browser integration run passed 113 cases; the first admin create case exceeded cold dev compilation's
startup budget, received an explicit scenario budget, and then passed on both projects (2/2). All 114
integration cases have successful verification across these runs. TypeScript and the production static build passed.
