# Isolate issue-driven coding from repository and deployment authority

Status: approved by the user on2026-10-24 after independent design review. Shared-dev deployment becomes a separate manual operation.

## Problem and preserved behavior

The existing agent pipeline combines editable issue discussion, auto-confirming code generation, generated builds, repository writes and shared-dev deployment on a persistent self-hosted runner. A trusted trigger does not authenticate comments fetched afterward, and restoring workflow files after generation does not contain generated code.

Preserve trusted requests for planning and a draft implementation PR, with a bounded correction loop and offline/synthetic verification. Remove access to the persistent dev host from generation and testing. Nothing in this design automatically merges a PR, resolves a review conversation or deploys production.

## Alternatives

1. Stop executable automation and retain planning only: strongest immediate containment, but removes draft implementation until rebuilt.
2. Separate disposable generation, test and publication jobs: recommended balance; retains draft PRs with a smaller trust boundary and no shared-host execution.
3. Keep the persistent runner behind a manual approval: insufficient on its own because approval does not isolate later generated code or prevent mutable-comment injection.

## Approved input is an immutable snapshot

Planning may read discussion as explicitly untrusted context. Implementation accepts only a selected plan comment ID, its exact content hash, an approved base commit, issue number and an authorized approver identity. Resolve the request through GitHub API data rather than interpolating issue/comment text into shell source. A label or the last20lines of discussion is never sufficient authorization.

Revalidate author permission and every snapshot binding before generation; fail closed if the comment was edited, deleted, replaced or associated with another issue. Later comments never silently enter the approved plan. Resuming a blocked run requires a new explicit snapshot approval. No unattended schedule can choose arbitrary approved-looking issues without these bindings.

## Separate execution domains

**Trusted control job:** run trusted default-branch code on a GitHub-hosted disposable runner, with only the API permissions needed to read requests and record status. Validate numeric IDs, repository identity and full commit hashes. Keep issue text in bounded JSON/files. Do not execute scripts from an issue branch.

**Generation job:** use a fresh hosted runner, read-only repository authority and checkout credentials disabled. Run the model-driven editing process inside a restricted container with no host home, Docker socket, deployment directories or Git credentials mounted; expose only the source workspace and bounded scratch. Apply time/process/memory/disk limits. Keep the real model credential in a trusted request gateway, not in generated code or the agent container. The gateway permits only configured generation endpoints/models and a bounded request/token budget; it never exposes credentials or accepts arbitrary destination URLs. Network from the untrusted container reaches only that gateway. If those controls cannot be established, generation fails closed rather than reverting to the self-hosted runner.

**Verification job:** use another fresh hosted runner with no model, publication or deployment secrets. Consume the validated proposal for that same run/base/plan hash. All generated builds, tests, dependency installation, npm lifecycle scripts and MSBuild targets run inside a restricted execution container, not directly on the Actions runner. Exclude runner home, Actions runtime credentials, Docker socket and host files; allow only workspace/scratch writes, explicit synthetic environment and isolated job-local services, with no external network. Never use the shared-dev Playwright URL. Bound duration, processes, memory and output. Generated test success is evidence, not permission to merge.

Network-enabled dependency preparation uses trusted control code to fetch exact approved lockfile/package identities as data into a bounded cache, without running project files, dependency hooks or imported build targets. It must never run npm install, dotnet restore or a generated manifest on the network-enabled host. Dependency changes require explicit approved identity/digest metadata; unresolved packages block verification instead of enabling network access. Install and execute prepared dependencies only in the offline restricted container. Apply the same no-host-credentials/mounts restrictions to generation dependency setup.

**Publication job:** use trusted default-branch publisher code on a fresh hosted runner. Only this job receives narrowly scoped contents/PR write authority. Read proposal files as data; never execute a proposal's scripts, hooks or package managers. Validate path and size limits, reject symlinks/submodules/traversal/binary payloads and edits to workflow/control-plane paths, bind the proposal to this repository/run/base/plan digest, and reject stale or cross-run artifacts. Use Git database APIs to create a dedicated branch and draft PR without applying attacker-controlled patches in a credentialed checkout. Never force-push unrelated branches, mark ready automatically or resolve conversations. A new base requires renewed validation, not a silent rebase of approval.

The gateway/provider credential remains a sensitive service dependency even though it is outside the agent process; its quota, logging and access controls are part of the acceptance checks. Hosted runners alone are not a complete sandbox.

## Artifact and lifecycle rules

Export only a bounded text-file proposal and metadata, not an arbitrary repository archive. The trusted exporter rejects links/special files and reads each allowed path within the proposal root; the publisher independently repeats validation. Do not upload homes, caches, environment dumps, model credentials or unrelated artifacts. Retain approved metadata and test summaries with a short documented expiry.

Pin shared input artifacts by exact run and digest. Validate the proposal again at the privileged publication boundary; checks performed only in an untrusted job are insufficient. Partial generation or failed validation produces a failed/blocked status and no branch publication. Retries use fresh execution environments and the same snapshot, with an overall attempt budget.

Publication has explicit validated, branch-created and draft-PR-created states. No repository write occurs before validation completes. Cancellation after branch creation may leave only that run's dedicated branch; a bounded idempotent reconciliation task can complete its draft PR or remove the orphan after verifying repository/run/digest ownership. It must never delete an existing unrelated branch or rewrite a reviewed PR. Record each publication state so an interrupted run is recoverable rather than claiming cross-API atomicity.

Existing feature branches are never resumed blindly: require review of the exact existing commit, or start a fresh run-specific branch. Preserve existing PRs and human comments without rewriting their history. Replace the shared home-directory cost ledger with per-run bounded accounting; logs omit credentials and private raw prompts.

## Shared-dev deployment decision

Recommended behavior: remove shared-dev deployment/seeding from the coding pipeline and retain it as a separate manual operation documented in environments.md. This also prevents concurrent automation from reseeding shared test data. The alternative is a separately approved deployment job consuming only a reviewed commit, behind a protected environment approval; it must never execute a fresh unreviewed proposal.

The user approved the recommended separate manual deployment behavior. No dev/prod host is changed by this design document.

## Implementation sequence

1. Replace implicit issue/comment-tail execution with the bound approval-snapshot parser and regression cases.
2. Add bounded proposal/export/publish contracts and independent privileged-side validation tests.
3. Introduce restricted disposable generation and synthetic verification jobs, including gateway/network/resource controls; fail closed on missing isolation.
4. Add the draft-only trusted publisher and bounded retry/accounting flow; remove automatic readiness and shared-host execution.
5. Update environment/operator docs for the chosen separate deployment behavior and migration of existing branches.
6. Review the workflow and hostile fixture tests independently before activation. No live workflow dispatch, paid model call or host deployment is required for regression tests.

## Acceptance checks

- An edited/deleted/wrong-author or wrong-issue plan, stale base and unrelated discussion cannot authorize implementation.
- Shell metacharacters in comments remain literal data; no expression interpolation enters shell code.
- Generated code cannot access write/deployment credentials, host files, the container engine or arbitrary network destinations.
- Tests cannot reach shared databases, auth servers, dev previews or paid providers.
- Forged artifacts, links, traversal, oversized files, workflow edits and cross-run/base substitution are rejected by trusted publisher code before any write.
- Timeout, cancellation, failed build, exhausted model budget and partial artifacts before publication leave no published branch or deployment. Interruption after validated publication starts can leave only its dedicated recoverable branch; reconciliation completes the draft PR or safely removes that verified orphan.
- Publication creates only a draft PR on its dedicated branch; existing review threads and production remain unchanged.
- Per-run metadata and operator docs make remaining external configuration requirements explicit; missing controls do not silently downgrade isolation.
