# Game Gemini and OpenAI provider rotation

## Goal

Keep the language game available when one AI provider or key has a temporary failure. The server can use up to ten Gemini keys and ten OpenAI keys. Players never enter or see provider credentials. Gemini is tried first, then OpenAI; inside each provider, healthy keys are attempted in rotation. Question generation and answer grading use the same provider order and credential pool.

## Approaches considered

1. Add more keys to the current Gemini-only transport. This is a small change, but it cannot provide provider diversity, and keys in one Google Cloud project still share that project's quota.
2. Add OpenAI as a fallback behind shared generation and grading contracts, with per-provider key rotation and temporary health state. This preserves the current Gemini behavior, gives OpenAI a chance when Gemini is unavailable, and keeps provider details out of the game hub. Recommended.
3. Alternate providers for each operation. This spreads normal traffic but makes provider cost and output behavior less predictable and does not prioritize the existing provider.

## Provider and key selection

- Attempt Gemini keys first in round-robin order, skipping keys in cooldown or marked invalid. If all currently available Gemini keys fail, attempt OpenAI keys using the same policy.
- Keep separate key state for each provider and slot: available, cooldown until a time, or disabled until process restart/configuration changes.
- Treat HTTP 408, 429, 5xx, timeouts, and connection failures as transient. Honor a valid `Retry-After`; otherwise apply bounded exponential cooldown with jitter. Advance to another key immediately. A later request may use a recovered key after its cooldown expires.
- Treat HTTP 400 as a provider request/model/schema failure and do not rotate through every key for the same malformed request. Treat 401/403 as a key configuration/access failure and disable that slot for the process lifetime. Treat other non-transient 4xx responses as request failures. Never include upstream response bodies or credentials in player-visible errors.
- If a provider returns HTTP success but its result is malformed or fails the existing domain validation, try the next credential/provider within the same overall request budget. Do not award points or advance the game unless a valid result is returned.
- Bound each operation by an overall deadline and maximum attempts: at most one attempt per configured slot, capped to ten per provider. Respect caller cancellation immediately. If the complete pool fails, preserve the answer/current round and emit the existing Retry-only error.
- Keep existing single-key settings as compatibility aliases while indexed settings are introduced. Empty slots are ignored. Do not require all twenty keys to be configured; a request may proceed with any one configured credential.

## Contracts and provider adapters

Keep `IGeminiSentenceService` and `IEntertainmentAnswerEvaluator` as the game-facing contracts. Add provider-specific adapters for OpenAI that return the same validated sentence, hints, evaluation scores, correction list, and explanation shapes as Gemini. A shared fallback coordinator invokes adapters in configured order and accepts only contract-valid results. The game hub, persisted rounds, UI, score rules, and retry-only player flow remain provider-agnostic.

Use OpenAI's server-side API with a configurable model. Require structured JSON output matching the existing bounded schemas. Keep API calls in the backend; never expose a key in browser code, SignalR payloads, logs, error text, or stored game records. Log provider name, slot index, operation, status category, elapsed time, and cooldown decision only.

## Configuration and deployment

- GitHub Actions secrets: `GEMINI_KEY_1` through `GEMINI_KEY_10`, and `OPENAI_API_KEY_1` through `OPENAI_API_KEY_10`.
- Continue accepting existing `GEMINI_KEY_2`, `GEMINI_KEY`, and `GEMINI_API_KEY` as legacy Gemini fallback inputs, plus the current `GAME_GEMINI_API_KEY` deployment value.
- Add configurable OpenAI model and endpoint defaults in backend configuration. Only configure secret values in GitHub Actions and the server's protected deployment `.env`; do not commit credentials.
- Sync version-controlled production Compose configuration as part of deploy. Map every configured slot into indexed .NET configuration keys. The deploy script validates that at least one provider key resolves and verifies the deployed container image and health.
- Manual provider diagnostic reports per-slot status category (working, rate-limited/cooling down, invalid/access denied, request/model error, timeout/network, malformed response). It makes one bounded minimal request per populated key, masks slot values, and never prints key strings or raw upstream response content.

## Failure and user behavior

Successful generation/evaluation returns the same game result as today. Provider failover stays invisible to the player. When all configured credentials fail, show the current safe retry message, keep the typed answer and round, and permit Retry only. Do not use sample content as an AI result and do not allow Next until evaluation succeeds.

If no credentials are configured, emit a safe server configuration category and the same retry-only player behavior. Diagnostics distinguish this from provider rate limits. A key that receives a transient 429 is cooling down, not permanently disabled; a later request can retry it when its wait expires. Daily/project quota exhaustion may last until Google's documented reset; switching to OpenAI can still allow the operation.

## Verification

- Unit-test key discovery/order, empty slots, round-robin progression, provider fallback, cooldown skip/expiry, `Retry-After`, bounded backoff, invalid-key disablement, malformed-output fallback, no configured keys, cancellation, and total attempt/deadline limits.
- Verify the same coordinator is used by generation and evaluation; verify valid output contract equivalence and server-side score validation.
- Verify player answer and current round survive pool exhaustion and that no Next event/points occur after failed evaluation.
- Verify logs and all client errors contain no key values, upstream response bodies, or submitted answer text.
- Exercise production Compose resolution with mixed legacy and indexed secrets, with Gemini only, OpenAI only, both providers, and no keys. Ordinary CI uses stubbed HTTP responses; live provider checks remain manual and bounded.
- After the owner adds secrets, run one minimal provider diagnostic per configured slot and a full production generation/evaluation smoke test.

## Explicit limits

Rotating multiple Gemini API keys from the same Google Cloud project does not increase that project's quota. Separate Google Cloud projects are required for independent project quotas. OpenAI keys under the same organization/project may likewise share limits. Provider rotation improves availability when a credential or provider has an independent failure; it does not multiply a shared project's quota. This feature does not automatically create API keys or alter billing.
