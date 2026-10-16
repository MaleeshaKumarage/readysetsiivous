# Gemini-only game AI reliability implementation plan

Spec: [2026-10-16 Gemini reliability design](../specs/2026-10-16-gemini-reliability-design.md)

## Tasks

1. Add typed Gemini provider error metadata (status/reason/code), bounded safe
   body parsing, and provider-neutral request handling limited to Gemini game
   calls. Keep credentials and raw error bodies out of logs/diagnostics.
2. Update the game AI coordinator to select Gemini only; distinguish shared
   quota, key-specific authentication, project access, invalid request/model,
   transient provider failure, and malformed output. Apply bounded
   `Retry-After`/backoff and model cooldown; skip key rotation for shared or
   ambiguous failures. Preserve the rolling request budget.
3. Add tests for Gemini-only selection, error parsing/redaction, retry bounds,
   cooldown sharing, local budget exhaustion, and cancellation. Retain existing
   round retry/idempotency behavior.
4. Make the live smoke workflow and both Compose environments Gemini-only. Make
   production deployment require a Gemini credential, clear retired OpenAI
   game secrets from the API host's managed `.env`, and update its tests.
5. Update language-game and environment docs to describe project-wide quotas,
   bounded retries, diagnostics, and the single-instance reservation
   assumption.
6. Run focused entertainment tests, API tests, deployment-script tests, and
   relevant site checks; review the diff to ensure unrelated user changes
   remain untouched.
