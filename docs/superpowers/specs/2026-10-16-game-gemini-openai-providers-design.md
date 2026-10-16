# Game Gemini and OpenAI provider rotation

## Goal

Keep the language game available when one AI provider or key has a temporary failure. The server can use up to ten Gemini keys and ten OpenAI keys. Players never enter or see provider credentials. Gemini is tried first, then OpenAI; inside each provider, healthy keys are attempted in rotation. Question generation and answer grading use the same provider order and credential pool.

## Approaches considered

1. Add more keys to the current Gemini-only transport. This is a small change, but it cannot provide provider diversity, and keys in one Google Cloud project still share that project's quota.
2. Add OpenAI as a fallback behind shared generation and grading contracts, with per-provider key rotation and temporary health state. This preserves the current Gemini behavior, gives OpenAI a chance when Gemini is unavailable, and keeps provider details out of the game hub. Recommended.
3. Alternate providers for each operation. This spreads normal traffic but makes provider cost and output behavior less predictable and does not prioritize the existing provider.

## Provider and key selection

- Every game generation and grading prompt enters one bounded FIFO queue. A single background worker processes one prompt at a time across both providers. The queue is process-local and held in memory; the current production deployment runs one API instance. Queue capacity is 20 waiting prompts. If full, reject the new operation with the safe retry message. The end-to-end operation deadline is 180 seconds and includes time waiting in the queue; expired or canceled items are skipped without calling a provider. An API restart drops queued work; the game retains its current round and submitted answer so the player can retry. If the API is later scaled to multiple instances, replace this with a distributed queue and a single shared consumer before scaling.
- For each queued operation, attempt Gemini keys first in round-robin order, skipping keys in cooldown or marked invalid. If no Gemini key returns a valid result, attempt OpenAI keys using the same policy. Both key health and round-robin position are shared by generation and grading.
- Use one-based indexed GitHub secrets `GEMINI_KEY_1` through `GEMINI_KEY_10`; append legacy `GEMINI_KEY`, `GEMINI_API_KEY`, and `GAME_GEMINI_API_KEY` after indexed Gemini slots, omitting empty or exact duplicate values, then cap the resolved pool at ten slots. Indexed slots always take priority over legacy aliases. `GEMINI_KEY_2` is slot 2, with no separate precedence rule. For OpenAI, use `OPENAI_API_KEY_1` through `OPENAI_API_KEY_10`. Empty slots are ignored. Logs identify resolved slots as provider plus pool index, without printing GitHub secret names or values.
- Keep separate key state for each provider and slot: available, cooldown until a time, or disabled until API restart. Configuration is loaded at startup; changing GitHub secrets takes effect after the next deployment/restart.
- Treat HTTP 408, 429, 5xx, timeouts, and connection failures as transient. Honor a valid `Retry-After`; otherwise apply bounded exponential cooldown with jitter. Advance to another key immediately. A later request may use a recovered key after its cooldown expires.
- Cap any `Retry-After` cooldown at 60 seconds for in-memory state; do not wait inside the active operation for that cooldown. Try other eligible keys and the other provider, then return the safe retry message if none can serve the request. Treat HTTP 400 as a provider request/model/schema failure: do not rotate through every key for the same provider request, but allow one attempt with the other provider because its model and schema support can differ. Treat 401/403 as a key configuration/access failure and disable that slot until API restart. Treat other non-transient 4xx responses as provider request failures. Never include upstream response bodies or credentials in player-visible errors.
- If a provider returns HTTP success but its result is malformed or fails the existing domain validation, try the next credential/provider within the same overall request budget. Do not award points or advance the game unless a valid result is returned.
- Bound each operation by an overall deadline and maximum attempts: at most one attempt per configured slot, capped to ten per provider. Respect caller cancellation immediately. If the complete pool fails, preserve the answer/current round and emit the existing Retry-only error.
- Configuration is loaded at API startup. Key-health state, provider round-robin cursors, and disabled slots reset on API restart. Do not require all twenty keys to be configured; a request may proceed with any one configured credential.

## Contracts and provider adapters

Keep `IGeminiSentenceService` and `IEntertainmentAnswerEvaluator` as the game-facing contracts. Add provider-specific adapters for OpenAI that return the same validated sentence, hints, evaluation scores, correction list, and explanation shapes as Gemini. A shared fallback coordinator invokes adapters in configured order and accepts only contract-valid results. The game hub, persisted rounds, UI, score rules, and retry-only player flow remain provider-agnostic.

Use OpenAI's server-side API with a configurable model. Require structured JSON output matching the existing bounded schemas. Keep API calls in the backend; never expose a key in browser code, SignalR payloads, logs, error text, or stored game records. Log provider name, slot index, operation, status category, elapsed time, and cooldown decision only.

## Configuration and deployment

- GitHub Actions secrets: `GEMINI_KEY_1` through `GEMINI_KEY_10`, and `OPENAI_API_KEY_1` through `OPENAI_API_KEY_10`.
- Continue accepting existing `GEMINI_KEY_2`, `GEMINI_KEY`, and `GEMINI_API_KEY` as legacy Gemini fallback inputs, plus the current `GAME_GEMINI_API_KEY` deployment value.
- Add a configurable OpenAI model in backend configuration and use the fixed official endpoint `https://api.openai.com/v1/responses`. Only configure secret values in GitHub Actions and the server's protected deployment `.env`; do not commit credentials.
- Sync version-controlled production Compose configuration as part of deploy. Map every configured slot into indexed .NET configuration keys. The deploy script validates that at least one provider key resolves and verifies the deployed container image and health.
- Operational logs report provider name, slot index, operation, safe failure category, elapsed time, and cooldown decision. Never log key strings, raw upstream response bodies, player answers, or full prompts. Provider diagnostic calls are not game prompts and are out of scope; test environments use stubbed responses, while production verification uses the normal game queue.

## Failure and user behavior

Successful generation/evaluation returns the same game result as today. Provider failover stays invisible to the player. When all configured credentials fail, show the current safe retry message, keep the typed answer and round, and permit Retry only. Do not use sample content as an AI result and do not allow Next until evaluation succeeds.

If no credentials are configured, emit a safe server configuration category and the same retry-only player behavior. Operational logs distinguish missing server credentials from provider rate limits. A key that receives a transient 429 is cooling down, not permanently disabled; a later request can retry it when its wait expires. Daily/project quota exhaustion may last until Google's documented reset; switching to OpenAI can still allow the operation.

## Verification

- Unit-test key discovery/order, empty slots, round-robin progression, provider fallback, cooldown skip/expiry, `Retry-After`, bounded backoff, invalid-key disablement, malformed-output fallback, no configured keys, cancellation, and total attempt/deadline limits.
- Verify the same coordinator is used by generation and evaluation; verify valid output contract equivalence and server-side score validation.
- Verify player answer and current round survive pool exhaustion and that no Next event/points occur after failed evaluation.
- Verify logs and all client errors contain no key values, upstream response bodies, or submitted answer text.
- Exercise production Compose resolution with mixed legacy and indexed secrets, with Gemini only, OpenAI only, both providers, and no keys. Ordinary CI uses stubbed HTTP responses; live provider checks use the normal queue and remain bounded.
- After the owner adds secrets, run a full production generation/evaluation smoke test through the normal queue.

## Explicit limits

Rotating multiple Gemini API keys from the same Google Cloud project does not increase that project's quota. Separate Google Cloud projects are required for independent project quotas. OpenAI keys under the same organization/project may likewise share limits. Provider rotation improves availability when a credential or provider has an independent failure; it does not multiply a shared project's quota. This feature does not automatically create API keys or alter billing.
