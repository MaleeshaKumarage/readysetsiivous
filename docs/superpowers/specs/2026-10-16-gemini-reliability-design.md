# Gemini-only game AI reliability design

Date: 2026-10-16
Status: Proposed; awaiting user review

## Problem

The language game must use Gemini for both question generation and answer
evaluation. The current coordinator still constructs Gemini and OpenAI provider
pools and falls back to OpenAI. It classifies HTTP 429 generically, cycles
through configured credential slots in some failure cases, and hides much of
the provider's structured error detail behind a generic unavailable response.
This can make a quota, account, model, credential, malformed-request, or
temporary-service problem look like the same failure to a player or operator.

Gemini quota is associated with the Google Cloud project and model, not an
individual API key. Rotating keys from the same project cannot add quota. The
server already has a bounded FIFO prompt queue, one consumer per process, a
database-backed rolling request budget, a low configured Gemini budget, and a
successful recent Gemini live smoke run. Reliability work should preserve
those protections and address failure classification, retries, diagnostics,
and shared quota behavior rather than promise that rate limits can be
eliminated.

## Goals and boundaries

- Use Gemini only for generation and evaluation; never silently switch to
  OpenAI.
- Preserve the current round and submitted answer when generation/evaluation
  fails. A player can retry; the game cannot advance until evaluation succeeds.
- Retry only errors that are plausibly transient, with a small bounded attempt
  count, exponential backoff with jitter, and a valid `Retry-After` when
  supplied.
- Stop sending requests during known project/model cooldown or exhausted
  configured budget. Do not rotate through keys to evade a shared project
  quota.
- Give operators safe, specific diagnostics without exposing API keys,
  prompts containing player data, or unbounded provider response bodies.
- Keep deterministic CI independent of live Gemini. Keep live provider checks
  explicit and Gemini-only.

Out of scope: increasing Google quota, changing billing, using OpenAI or another
provider, enabling Google logging/billing, guaranteeing provider availability,
or rewriting the game queue as a distributed queue while production runs one
API instance. If deployment scales to multiple API instances, shared atomic
quota reservation becomes a separate required change.

## Approaches considered

1. **Gemini-only with typed failure handling (recommended).** Remove OpenAI
   selection from the game coordinator, parse Gemini's structured error code
   and status, apply bounded retry/cooldown by failure class, and surface a
   safe diagnostic. This directly matches the required provider and avoids
   duplicate calls against shared project quota.
2. **Keep provider fallback but improve errors.** Rejected because the user
   explicitly chose to disable OpenAI fallback and hidden provider switching
   obscures which service produced the result.
3. **Retry aggressively and rotate keys.** Rejected because keys in one
   project share the model quota; this can amplify a throttle and consume
   requests without increasing capacity.

## Design

### Request path

Generation and evaluation continue through the existing bounded FIFO queue.
The coordinator submits exactly one provider pool: Gemini. Credential slots
may still be tried for credential-specific failures such as an invalid key,
but not for model/project quota exhaustion or provider-wide cooldown. Requests
share a single quota lane for the configured Gemini model. The API's existing
database usage window remains the conservative RPM/RPD gate; the current
defaults remain 3 requests per minute and 15 per rolling 24 hours unless
deployment configuration is intentionally changed after checking the exact
model and project quota.

The queue is process-local and the current deployment is single-instance. The
database-backed request timestamps survive restarts, but reservation is not
atomic across multiple API replicas. Deployment documentation must state this
assumption. If the service is scaled horizontally, replace the read/modify/write
reservation with an atomic shared reservation before doing so.

### Failure classification and retry

Read Gemini's bounded JSON error body and `Retry-After`, if present. Parse the
structured HTTP status and Gemini error `status`/`reason` fields first; use a
sanitized message only as a fallback for known quota wording. Store only the
HTTP status, normalized Gemini error code, a sanitized/truncated message, and
retry/cooldown timing in diagnostics. Never store the request key or echo the
raw body. Preserve provider status and code through the internal unavailable
exception to the existing hidden error-details UI. An unrecognized 429 is
treated as a shared model-level throttle: honor `Retry-After` if present;
otherwise apply a conservative model cooldown and one bounded retry at most.
Do not rotate keys for an unrecognized 429.

Classify outcomes as follows:

| Class | Examples | Behavior |
|---|---|---|
| Transient request limit | HTTP 429 with `rate_limit_exceeded` or `too_many_requests`; HTTP 503 | Respect `Retry-After`. Retry once only if the delay fits inside the remaining 55-second provider budget; otherwise set a shared model cooldown and return a retryable unavailable result. Without `Retry-After`, use exponential backoff with jitter, bounded by that same budget. |
| Exhausted quota | HTTP 429 with an explicit daily/project quota code or known daily/project quota wording; configured local RPD exhausted | Do not retry immediately or try another same-project key. For local RPD, return the exact next eligible time from the oldest timestamp in the rolling window. For Google quota, use an explicit reset time if supplied. Google's documented midnight-Pacific reset applies only when the response is known to represent RPD; do not infer that reset for generic project, spend, or model quota. If no reliable reset is supplied, record retry time as unknown and tell the user to retry later. |
| Credentials/project access | HTTP 401 or structured key-invalid/unauthorized code; HTTP 403 or structured permission/access code | Do not retry the same key. Try another configured Gemini slot only for an explicit key-specific invalid/unauthorized error. A 403 permission/project-access error and any ambiguous 401/403 stop the pool; do not assume another key will fix project access. |
| Invalid request/model | HTTP 400/404/422 or invalid model/schema error | Do not retry. Return a sanitized diagnostic identifying the request/model configuration class. |
| Network/timeout/5xx | Connection failure, timeout, HTTP 500/502/503/504 | One bounded retry with jitter where safe, then a short shared cooldown and retryable diagnostic. Caller cancellation propagates without retry. |
| Invalid provider output | Malformed envelope or schema mismatch | Do not repeat indefinitely. Fail the current operation safely and allow explicit player retry; log a bounded validation category. |

Provider response content and diagnostic text must be sanitized and length
limited. Because the UI already supports an expandable error detail, retain a
safe message there and show a plain-language top-level status. Quota failures
must not be labeled as temporary minute-level rate limits when Google signals
daily/project exhaustion.

### Game behavior

No sample question or local evaluation fallback is used. If generation fails,
the pending transition remains retryable and no next round is committed. If
evaluation fails, retain the submitted answer and current round; only a
successful validated evaluation can unlock the next question. Repeated submit
or retry requests remain idempotent and cannot award score twice.

### Configuration and operations

Remove OpenAI game provider selection and OpenAI key/model/rate-limit wiring
from the live smoke workflow and runtime game documentation. Do not delete
unrelated repository secrets. The live smoke workflow should have a single
Gemini choice and fail early with a clear missing-key message. It should make
the existing bounded generation and evaluation checks and report only
redacted status/code/timing details.

Document that all Gemini keys from the same Google project share quota; the
AI Studio model/project table is authoritative; Google RPD resets at midnight
Pacific; the application rolling 24-hour cap is intentionally more
conservative; and transient retries are bounded. Do not enable billing or
provider logging as part of this change.

## Validation

- Unit tests cover Gemini error parsing and sanitized diagnostics for
  rate-limit, daily quota, auth, permission, invalid model/request, provider
  outage, timeout, malformed body, and missing retry headers.
- Retry tests verify bounded attempts, `Retry-After` handling (including delay
  beyond the request budget), jitter bounds, parser precedence and unknown
  429/403 defaults, no retry on non-transient classes, no same-project key
  cycling after shared quota errors, and cancellation propagation.
- Coordinator tests prove concurrent calls share one model cooldown, calls
  arriving during cooldown do not send HTTP requests, local rolling-budget
  exhaustion returns the next eligible time, and only one API instance may
  safely reserve the current non-atomic database usage window.
- Coordinator tests prove OpenAI credentials are ignored and no OpenAI request
  is sent, while Gemini key slots still work for credential-specific failure.
- Game tests prove failed generation does not commit/advance, failed
  evaluation retains the answer and blocks advancement, and retry after
  success awards points once.
- Live smoke workflow runs Gemini only; regular CI stays fake-driven and does
  not spend API quota.
- Run focused .NET tests and the complete relevant API/site checks before
  merge.

## Known limitations

No client-side or server-side queue can prevent Google from throttling requests
outside the application's view, enforcing an account/project cap lower than
the observed dashboard value, or temporarily failing. The change will prevent
avoidable bursts and duplicate fallback calls, classify failures accurately,
and preserve retryable game state; it cannot guarantee provider availability.

## References

- [Gemini API rate limits](https://ai.google.dev/gemini-api/docs/rate-limits)
- [Gemini API troubleshooting](https://ai.google.dev/gemini-api/docs/troubleshooting)
- [Gemini API errors](https://ai.google.dev/gemini-api/docs/api-errors)
