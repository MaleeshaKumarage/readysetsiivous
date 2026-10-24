# Bounded draft implementation pipeline

`agent-pipeline.yml` accepts only manual dispatch from the repository default
branch. Every job uses trusted control scripts at the dispatch commit, with
checkout credential persistence disabled. It creates a new run-specific draft
PR; it never resumes an old feature branch, marks a PR ready, resolves review
threads, deploys or seeds shared dev. See [manual dev deployment](environments.md#updating-the-shared-dev-preview).

## Approval and dispatch

Write or select one exact plan comment on the intended issue. Planning and
clarification remain a human review step: copy a proposed plan into a trusted
maintainer comment after checking it. Discussion and later comments are not
implementation instructions. Both the selected comment's author and dispatch
actor (the triggering actor on a rerun) must satisfy the contract's current repository permission checks.

Record the issue number, numeric comment ID, full approved base commit SHA and
SHA256 of the comment's exact UTF8 body. Hash the decoded API body without
adding a newline, normalizing whitespace or copying terminal-rendered text:

```bash
# GH_REPO is owner/repository; COMMENT_ID is the selected numeric comment ID.
gh api "repos/$GH_REPO/issues/comments/$COMMENT_ID" > selected-comment.json
python3 - <<'PY'
import hashlib, json
from pathlib import Path
body = json.loads(Path('selected-comment.json').read_text(encoding='utf-8'))['body']
print(hashlib.sha256(body.encode('utf-8')).hexdigest())
PY
```

Keep that local file private if the plan contains private information. Review
its issue association and author. In Actions, select `agent-pipeline`, choose
the default branch, and provide all four required fields: `issue-number`,
`plan-comment-id`, `plan-sha256`, `base-sha`. Editing/deleting the plan or changing
the base requires a new approval and dispatch. Retries retain exact bindings;
run-attempt binding prevents reuse of an earlier attempt's artifacts.

## Activation prerequisites

Source implementation is not evidence that execution is activated. An operator
must first prepare and review the execution images, offline caches and host
controls. Set repository variables `AGENT_PYTHON_IMAGE` and
`AGENT_TOOLCHAIN_IMAGE` to approved immutable `image@sha256:<64 hex>` references.
The workflow validates each digest reference and pulls it on the trusted host
before restricted execution. Images must be publicly pullable; this workflow
does not configure registry credentials. Use a separately reviewed read-only
package-pull design before using private images. The generation image needs the
trusted Python entry point/runtime dependencies.
The toolchain image must contain approved SDKs, package data and offline caches
bound to the exact approved base; missing packages block verification. It must
contain `/opt/agent-cache-manifest.json` with exactly
`{"schema":1,"base_sha":"<approved full SHA>","nuget":"/opt/offline-nuget","npm":"/opt/npm-cache"}`,
the two named package caches and preinstalled browsers under `/opt/playwright`.
The workflow also supplies `AGENT_SOURCE_BASE_SHA` for the approved base. Do not
fetch generated dependency manifests on a network-enabled runner. Dependency
changes are rejected by the first-version proposal allowlist.

The disposable Ubuntu host must support Docker resource limits, non-root users,
read-only roots, dropped capabilities, no-new-privileges, bounded scratch, and
host firewall enforcement (the generation wrapper runs with narrowly preserved
image/key environment under sudo to establish these rules). Generation's internal bridge alone is insufficient:
the sandbox must block host access and forwarding except the bounded trusted
gateway. Missing enforcement fails closed. Verification uses no network and
receives no model or deployment secrets. Generated builds/tests execute only
through `agent_sandbox.py`, inside the restricted container.

Set `DEEPSEEK_API_KEY` only after validating these controls with a dummy provider
and approved images. Only the trusted host gateway in the generation step
receives this secret; the agent container receives no provider or GitHub token.
No paid provider call, live dispatch or deployment is required for source tests.
Ordinary CI runs the standard-library `test_agent*.py` regressions without keys.

## Artifacts and publication

Control captures a canonical schema-1 snapshot and exports its trusted hash.
Generation rechecks exact artifact API metadata and current approval before a
paid call. Each handoff downloads the exact artifact ID from the same run;
the trusted artifact helper checks API provenance, hashes the actual ZIP bytes,
and extracts only one bounded expected JSON file without following archive paths.
The contract then checks the canonical snapshot hash, name, repository, run,
attempt and dispatch SHA.
Artifacts are retained for three days and contain only bounded snapshot,
proposal and verification JSON. They do not contain caches or environment dumps.

A separate fresh host verifies the proposal offline and records a receipt bound
to its exact contents, snapshot and base. Only publication has contents/PR write
permissions. It independently authenticates all three artifacts and validates
the receipt and source-only paths before any Git database API write. It never
executes proposal content. Publication uses a dedicated branch and draft PR;
no forced push or automatic merge is allowed.

A failed generation, missing cache, failed verification or invalid artifact
blocks publication. Cancellation after validated branch creation may leave that
run's branch. Re-run only with the same verified bindings; inspect publication
state before manually reconciling an orphan. Never delete unrelated branches or
rewrite an existing reviewed PR. Keep failure logs free of raw prompts and keys.

The bounded correction loop is a new reviewed plan/comment/hash/base dispatch;
there is no unattended issue polling or mutable comment-tail approval.

Generation is a bounded JSON source-proposal interface without a shell tool.
It does not provide the former unrestricted agent's complete editing/tool loop.
Human review and a new explicitly approved dispatch handle corrections.

## Verification evidence

The review-integrity workflow also runs opt-in real Linux/Docker checks on a
disposable GitHub-hosted runner. It resolves the public Python test image to a
digest before use, verifies non-root/read-only/resource/network restrictions,
and probes the actual gateway firewall using a synthetic key without requesting
any completion. This is separate from operator approval of activation images.
The complete offline application build still requires the reviewed toolchain
image and base-specific caches described above.
