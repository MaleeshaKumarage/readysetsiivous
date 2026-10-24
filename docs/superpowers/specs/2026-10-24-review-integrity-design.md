# Preserve AI review and human conversation integrity

Approved scope: the user approved the first remediation batch after the infrastructure/security review: provider failures must not become clean reviews, and threads must remain open until a reviewer verifies a fix.

## Design

The provider CLI returns nonzero and emits no review text for missing credentials, HTTP/transport failures, malformed payloads, empty content or truncated generation. Diagnostics omit response bodies and exception details to avoid copying secrets/prompts into CI logs. Successful content and cost accounting retain their existing interfaces. The shell wrapper propagates provider failure instead of allowing cost logging to mask it.

The PR workflow validates output before posting. Only the exact standalone `NO_CRITICAL` sentinel represents a completed clean review; otherwise every finding must have the requested FILE/positive LINE/FIX structure. Empty, partial, mixed clean/findings and prose responses fail. This gate validates format, not the correctness of the model's conclusions.

Delete the file-based automatic conversation resolver and its workflow invocation. Remove the agent pipeline's automatic resolution after an aider edit as well; neither file modification nor a successful AI review is evidence that a review concern was fixed. The explicit manual resolver utility remains available, but no workflow invokes it. The clean-review comment does not claim merge readiness.

Alternatives considered: retaining file-based automatic resolution remains unsound; asking AI to judge all existing comments adds another uncertain dependency. Manual reviewer verification is the smallest dependable policy.

## Implementation and validation

1. Refactor the shared provider CLI into a testable main function and fail closed before printing content.
2. Propagate failure through its shell wrapper, add the review-format gate, and delete automatic resolution.
3. Add offline mocked provider tests and CI wiring for those tests plus shell syntax validation.
4. Independently inspect the scoped diff and verify only intended files enter the commit.

Regression cases cover valid clean/findings responses, missing key with no HTTP call, provider429/network/timeout errors, malformed/truncated content, ambiguous review markers, partial finding blocks and preservation of human threads. Tests make no provider or GitHub requests. Runtime application, deployment, platform provisioning and production data are outside this batch.
