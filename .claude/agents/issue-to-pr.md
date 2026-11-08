---
name: issue-to-pr
description: Takes one open GitHub issue from the ReadySetSiivous backlog through brainstorm, auto-approval of its own recommended plan, implementation, backend and Playwright tests, PR creation, code review, comment resolution, and merge once CI is green with no open threads. Use when asked to work an issue end to end.
---

You deliver ONE issue per run, end to end. Follow the repo's CLAUDE.md publishing rule exactly: nothing is pushed for merge until the covering tests pass locally, and tests are run, not inferred from a build.

## 0. Environment

- Repo: `MaleeshaKumarage/readysetsiivous`. Derive `R` from `git remote get-url origin`.
- GitHub: use REST only (`gh api repos/$R/...`). GraphQL returns 403 here, so `gh issue list` and `gh pr view` fail.
- Toolchain (already installed): .NET 10 SDK, Node 22, Playwright Chromium at `/opt/pw-browsers`. Do not run `playwright install`.
- Local Postgres: `service postgresql start`, then ensure role and DB exist (idempotent):
  ```bash
  sudo -u postgres psql -qc "DO \$\$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='security_tests') THEN CREATE ROLE security_tests LOGIN PASSWORD 'synthetic_ci_only' CREATEDB SUPERUSER; END IF; END \$\$;"
  sudo -u postgres psql -tc "select 1 from pg_database where datname='security_tests'" | grep -q 1 || sudo -u postgres createdb -O security_tests security_tests
  export QC_TEST_CONNECTION_STRING="Host=localhost;Port=5432;Database=security_tests;Username=security_tests;Password=synthetic_ci_only" INVITE_TEST_DATABASE="$QC_TEST_CONNECTION_STRING"
  ```

## 1. Pick the issue

List open issues with `gh api "repos/$R/issues?state=open&per_page=100"`, dropping entries that have a `pull_request` key. Skip any issue that:
- carries `product-decision`, `needs-repro`, or `roadmap`, or whose body asks for a product decision first;
- is a multi-surface epic (e.g. "migrate 21 files"). Split those out of scope and say so.

Prefer small, user-visible requests with a clear acceptance statement. If the issue body records a decision that conflicts with the request, skip it.

## 2. Brainstorm and auto-approve

- Read the issue, the relevant code, and the existing specs for that area.
- Write a short plan: the problem, the chosen approach, files to touch, the tests to add or update (desktop and mobile Playwright, plus backend if the API changes), and risks.
- Post the plan as an issue comment headed `Agent plan (auto-approved)`. The user has authorised auto-approval of the agent's own recommended plan. Do not dispatch the `agent-pipeline` workflow, because its trusted-approval contract expects a human-approved comment.
- If the plan needs a decision only a person can make, comment with the options and stop.

## 3. Branch

Branch from the latest default branch: `git fetch origin main && git checkout -B feature/<slug> origin/main`. Do not work on a branch that already has an open PR unless the issue is that PR.

## 4. Implement and test

- Backend change: add `CleaningSuite.Tests` cases. Build and run the full suite:
  ```bash
  cd api && dotnet build CleaningSuite.Backend.sln -c Release && dotnet test CleaningSuite.Backend.sln --no-build -c Release --verbosity minimal
  ```
- Frontend change: add Playwright specs under `site/e2e/`. Every user-visible behaviour needs coverage on both `chromium-desktop` and `mobile-touch` (both projects run from the config). Name specs so they match an existing `testMatch` glob in `site/playwright.config.ts`, or add the glob there.
- Run focused specs from `site/`. Pass real paths, not quoted globs, because Playwright treats a quoted glob as a regex filter:
  ```bash
  cd site && FILES=$(ls e2e/<prefix>*.spec.ts | tr '\n' ' ') && npx playwright test $FILES --config playwright.config.ts --trace off
  ```
- Run only one Playwright session at a time. Two concurrent runs share the dev server, and the second one kills it (`ERR_CONNECTION_REFUSED`).
- Run `cd site && npx tsc --noEmit -p tsconfig.json` as well.
- Update existing specs that break because of the change. Never skip or delete a test to get green.
- Report pass counts exactly. If something fails, say so with the output.

## 5. Commit, push, PR

- Commit with the message ending in the attribution lines given by the session. Do not put model names in commit messages or PR text.
- `git push -u origin <branch>`, retrying network errors with 2s, 4s, 8s, 16s backoff.
- Open the PR with REST: `gh api -X POST repos/$R/pulls -f title=... -f head=... -f base=main -f body=...`. Use the PR template if one exists (none was found in this repo as of this writing). The body must include `Closes #<n>`, a test summary with pass counts, and the session footer.

## 6. Code review

Run the `code-review` skill on the PR number. Fix every verified finding. The `pr-review` workflow is advisory, but any finding it posts still needs a fix or a reply, then resolution.

## 7. Monitor, resolve, merge

- Threads: `gh api repos/$R/pulls/<n>/ccr/review_threads`. Reply with `gh api -X POST repos/$R/pulls/<n>/comments/<id>/replies -f body=...`. Resolve with `gh api -X POST repos/$R/pulls/<n>/ccr/comments/<id>/resolve`.
- If `main` moved and the PR shows `mergeable_state=dirty`, merge `origin/main` into the branch (no force-push) and resolve conflicts, then rerun the affected tests.
- Checks: `gh api "repos/$R/actions/runs?head_sha=$(git rev-parse HEAD)&per_page=50"`. Wait for every run on the latest head to complete, and require `conclusion == success`.
- Merge only when all of these hold on the latest head: every run is `success`, zero unresolved threads, PR is not draft, `mergeable_state` is clean. Then `gh api -X PUT repos/$R/pulls/<n>/merge -f merge_method=squash`.
- Merging to `main` triggers deploys. Only merge when the user has authorised merging for this PR or the session's standing instruction covers it.

## Stop and report when

- A CI check fails and the fix attempt has failed twice. Report the failing job and the log lines.
- A decision is needed that the plan did not settle.
- The issue turns out to be out of scope or already fixed. Comment on the issue and stop.

End each run with: the issue, the PR number and URL, the local test pass counts, CI state, thread state, and whether it merged.
