---
name: issue-to-pr
description: Takes one open GitHub issue from the ReadySetSiivous backlog through brainstorm, auto-approval of its own recommended plan, implementation, backend and Playwright tests, PR creation, code review, comment resolution, and merge once CI is green with no open threads. Runs autonomously and never waits for user input. Use when asked to work an issue end to end.
---

You deliver ONE issue per run, end to end. Follow the repo's CLAUDE.md publishing rule exactly: nothing is pushed for merge until the covering tests pass locally, and tests are run, not inferred from a build.

## 0. Environment

- Repo: `MaleeshaKumarage/readysetsiivous`. Derive `R` from `git remote get-url origin`.
- GitHub: use REST only (`gh api repos/$R/...`). GraphQL returns 403 here, so `gh issue list` and `gh pr view` fail.
- Toolchain (already installed): .NET 10 SDK, Node 22, Playwright Chromium at `/opt/pw-browsers`. Do not run `playwright install`.
- Local Postgres: `service postgresql start`, then ensure role and DB exist (idempotent):
  ```bash
  sudo -u postgres psql -qc "DO \$\$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='security_tests') THEN CREATE ROLE security_tests LOGIN PASSWORD 'synthetic_ci_only' CREATEDB; END IF; END \$\$;"
  sudo -u postgres psql -qc "ALTER ROLE security_tests NOSUPERUSER CREATEDB;"
  sudo -u postgres psql -tc "select 1 from pg_database where datname='security_tests'" | grep -q 1 || sudo -u postgres createdb -O security_tests security_tests
  export QC_TEST_CONNECTION_STRING="Host=localhost;Port=5432;Database=security_tests;Username=security_tests;Password=synthetic_ci_only"
  export INVITE_TEST_DATABASE="$QC_TEST_CONNECTION_STRING"
  ```

- Push check: after every push, `git ls-remote --heads origin <branch> | cut -f1` must equal `git rev-parse HEAD` (ls-remote prints `<sha>\trefs/heads/<branch>`, so compare the first field only). If the stop hook still reports unpushed commits, the cause is local tracking config (`remote.origin.fetch` only maps `main`). Add a refspec for the branch, `git config --add remote.origin.fetch '+refs/heads/<branch>:refs/remotes/origin/<branch>'`, then `git branch --set-upstream-to=origin/<branch>`. Do not force-push.

## 1. Pick the issue

List open issues with `gh api "repos/$R/issues?state=open&per_page=100"`, dropping entries that have a `pull_request` key. Prefer small, user-visible requests with a clear acceptance statement. Among those, take the oldest eligible one.

Before starting, claim the issue so two runs cannot both work it:
- Skip it if it has the label `agent-in-progress`, or if an open PR body contains `Closes #<n>`, `Fixes #<n>` or `Resolves #<n>`.
- Otherwise add the label (create it if missing, `gh api -X POST repos/$R/labels`) and comment `Agent run started`. Remove the label when the run finishes, whether it merged or not.

Issues labelled `product-decision`, `needs-repro`, or `roadmap`, or whose body asks for a decision first, are still eligible. The agent makes the decision itself (see step 2). Skip only issues that are already fixed on `main` or are plainly duplicates, and comment on those before moving to the next candidate. If an epic is too large for one PR, deliver its first small, self-contained chunk and open a follow-up issue for the rest.

## 2. Brainstorm and auto-approve

- Read the issue, the relevant code, and the existing specs for that area.
- Write a short plan: the problem, the chosen approach, files to touch, the tests to add or update (desktop and mobile Playwright, plus backend if the API changes), and risks.
- Post the plan as an issue comment headed `Agent plan (auto-approved)`. The user has authorised auto-approval of the agent's own recommended plan. Do not dispatch the `agent-pipeline` workflow, because its trusted-approval contract expects a human-approved comment.
- This self-approval is intentional and was requested by the user. Keep the `Agent plan (auto-approved)` heading so it is always visible which plans were approved by the agent.
- Never wait for a person. When the issue or code leaves a question open, pick the recommended option (the one with the smallest blast radius that satisfies the issue's stated request), state the choice and the alternatives you rejected in the plan comment, and proceed. Where the issue explicitly records a decision, follow it.

## 3. Branch

Branch from the latest default branch. Never use `git checkout -B`, because it silently resets an existing branch.
```bash
git fetch origin main
NAME=feature/<slug>
if git show-ref --verify --quiet refs/heads/$NAME || git ls-remote --exit-code --heads origin $NAME >/dev/null; then NAME=$NAME-$(date +%s); fi
git checkout -b "$NAME" origin/main
```
Never work on a branch that already has an open PR unless the issue is that PR.

## 4. Implement and test

- Backend change: add `CleaningSuite.Tests` cases. Build and run the full suite:
  ```bash
  cd api && dotnet build CleaningSuite.Backend.sln -c Release && dotnet test CleaningSuite.Backend.sln --no-build -c Release --verbosity minimal
  ```
- Known gap: Playwright specs mock the API. They prove the UI sends the right request, not that the real API accepts it. For a change that alters an API contract, the backend test must cover the new contract directly, and the PR body must say that UI tests use mocks.
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

## Budgets

Each run stops making changes when any budget runs out, records the state on the PR and the issue, and removes the `agent-in-progress` label. Budgets are not waiting: the run is finished.
- Fix attempts per failing check: 3.
- Fix commits per issue: 8.
- Wall-clock time per run: 3 hours.
- Test reruns for a flaky infrastructure failure: 1.

## 7. Monitor, resolve, merge

- Threads: `gh api repos/$R/pulls/<n>/ccr/review_threads`. Reply with `gh api -X POST repos/$R/pulls/<n>/comments/<id>/replies -f body=...`. Resolve with `gh api -X POST repos/$R/pulls/<n>/ccr/comments/<id>/resolve`.
- If `main` moved and the PR shows `mergeable_state=dirty`, merge `origin/main` into the branch (no force-push) and resolve conflicts, then rerun the affected tests.
- Checks: `gh api "repos/$R/actions/runs?head_sha=$(git rev-parse HEAD)&per_page=50"`. Wait for every run on the latest head to complete, and require `conclusion == success`.
- Merge only when all of these hold on the latest head SHA (not an older one): every run on that SHA is `completed` with conclusion `success` (`pr-review` is advisory and must still not be failing), no run is `in_progress` or `queued`, zero unresolved threads, PR is not draft, `mergeable_state` is `clean`. Then `gh api -X PUT repos/$R/pulls/<n>/merge -f merge_method=squash -f sha=<head-sha>`, so the merge is refused if the head moved.
- Deploy guard: run `python3 scripts/agent_deploy_guard.py <n>` before merging. It reads the push-to-main path filters of the deploy workflows on `origin/main` and tests the PR's files against them. Exit code 2 means the merge would deploy (production API build, or the GitHub Pages site). Do not merge then. Comment on the PR with the JSON output and finish the run with the PR open for a person to merge. Exit code 1 is an error, so do not merge either. The guard's path list comes from the workflows, so it does not go stale when a workflow changes.
- Merging to `main` can trigger deploys. The standing instruction is to merge once the gates above pass, except when the deploy guard fires (see below).

## Never stop for input

The agent does not pause to ask a person anything. Each situation has an autonomous path:

- **Ambiguous requirement:** choose the recommended option, record it in the plan comment, continue.
- **Failing CI or tests:** diagnose from the logs, fix, rerun. Keep iterating while each attempt changes something real. If a check is failing for a reason outside the change (an infrastructure or flaky-environment failure), rerun it once. If it still fails, fix the root cause in this branch or split out the unrelated part into its own issue, and continue.
- **Review finding:** fix it if it is valid, or reply on the thread with the evidence it is not and resolve it. Never leave a thread open.
- **Conflict with `main`:** merge `origin/main` and resolve the conflict, keeping both sides' intent.
- **Issue already done or out of scope:** comment on the issue with the evidence, close nothing, and pick the next eligible issue.
- **Tooling problem (missing package, service down):** install or start it, as in step 0, and continue.

The only thing the agent reports as blocked is a hard external failure it cannot work around (for example GitHub returning errors on every call for several minutes). Even then it records the state in the issue and the final summary, and retries with backoff first.

End each run with: the issue, the PR number and URL, the local test pass counts, CI state, thread state, and whether it merged.
