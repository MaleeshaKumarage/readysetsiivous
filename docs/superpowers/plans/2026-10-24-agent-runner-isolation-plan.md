# Coding-agent isolation implementation

## Primary goal

An authorized maintainer can request a bounded draft implementation without letting issue discussion or generated code control repository credentials or the shared dev host. Dev deployment remains a separate manual operation.

## Phase 0: documentation discovery

How this serves the primary goal: establishes real isolation and publication APIs before changing trust boundaries.

Completed read-only discovery of current agent workflow, CI synthetic test patterns, Docker run/internal network documentation, GitHub artifact metadata and Git database APIs. Allowed interfaces: Docker read-only/cap-drop/no-new-privileges/user/pids/memory/cpus/tmpfs/network controls; urllib fixed-host GitHub REST; GitHub blob/tree/commit/ref creation and draft pull requests. An internal Docker bridge still allows host access, so generation must additionally enforce host/forward firewall rules and fail closed if enforcement is unavailable. See official Docker run/network-create references and GitHub REST Git/artifact/collaborator permission references cited in implementation docs.

## Phase 1: implementation contracts and isolated execution

How this serves the primary goal: an exact approved input and bounded data-only output replace mutable discussion and credentialed execution.

Delegate independent units with nonoverlapping files:

- contracts: scripts/agent_contract.py and tests/test_agent_contract.py. Capture/revalidate immutable authorized snapshots, strict proposal schema/path/text/size validation, fixed-host REST, exact same-run artifact verification and idempotent Git database draft-only publication.
- execution: scripts/agent_gateway.py, scripts/agent_generate.py, scripts/agent_sandbox.py and matching tests. Fixed-provider bounded gateway; source-edit proposals as JSON data, no shell tool; restricted generation with gateway-only networking plus firewall enforcement; offline restricted verification; bounded host-side export, no copying generated files into privileged checkout.
- workflow integration after interfaces settle: replace agent-pipeline.yml with default-branch-only manual approved-snapshot control, separate hosted generation/verification/publication jobs, immutable artifact IDs/digests, minimal permissions and no checkout credential persistence. Update operator docs, static security CI checks and activation prerequisites.

Shared schema1: snapshot has repository, repository_id, run_id, run_attempt, issue, comment_id, plan_sha256, plan_body, plan_author_id, plan_updated_at, base_sha, approver and approver_id. Proposal has schema, repository, run_id, run_attempt, base_sha, snapshot_sha256 and files[{path,content}], where content is UTF8 text or null for deletion. snapshot_sha256 hashes canonical sorted compact UTF8 JSON. Bound changes to20files,128KiB perfile and1MiB total. Source-only allowlist excludes workflow/control/deployment paths and dependency manifests; first version rejects dependency changes rather than fetching new unapproved packages.

Execution images must be operator-approved immutable digests, with base-bound offline package caches for verification. Missing images/caches/firewall capability block the run; never silently enable network or revert to the old runner. Tests use mock transports/provider responses, not real paid calls or deployed services. Document required image preparation explicitly rather than claiming a working production activation without it.

Verification: hostile schema/path/link/size/provenance/replay fixtures; API request traces proving no writes before validation; sandbox command/resource/firewall tests; wrapper and workflow YAML checks; Linux container tests with dummy provider and no secrets. Anti-patterns: self-hosted agent execution, raw event interpolation in shell, persist-credentials, mutable plan tails, generated code under write token, shared-dev seed, automatic PR readiness/conversation resolution, unauthenticated artifact substitution.

## Phase 2: independent verification and publication

How this serves the primary goal: confirms the trust split before merging new automation.

Fresh verification, anti-pattern and code-quality agents inspect the complete implementation, run offline tests and resolve source-backed findings. Commit only verified changes, push a separate PR, inspect CI/comments and retain explicit activation requirements. No paid model call, live pipeline dispatch, host deploy or production secret change is required or authorized for validation.
