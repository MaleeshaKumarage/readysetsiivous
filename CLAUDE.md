# ReadySetSiivous / Cleaning Suite — working rules

## Publishing rule (mandatory)

- No feature, fix or UI change is published (pushed for merge, opened as a PR for review, or deployed) until it is covered by Playwright tests that pass.
- Required coverage: each user-visible behaviour the change adds or alters has a Playwright test on both `chromium-desktop` and `mobile-touch` projects, unless the change is backend-only (then backend tests are required instead).
- Run the focused specs locally from `site/` with `npx playwright test <spec> --config playwright.config.ts` and report the pass count. A spec that was edited to match new behaviour must also pass.
- Existing specs that break because of the change are updated in the same PR, not skipped or deleted to get green.
- Never claim a feature is verified from a type check or build alone.

## Other conventions

- One small PR per chunk. Merge only after every latest-head CI check is green and no review thread is unresolved.
- The automated `review` check (AI review job) is advisory, not a required check. Its failure does not block a merge, but any finding it posts still needs a fix or a reply on its thread before the thread is resolved.
- Preserve tenant and role boundaries and existing financial snapshots.
