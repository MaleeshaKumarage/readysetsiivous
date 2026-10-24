# Language game

The realtime language game generates a new English sentence and Finnish word hints for each group round. The active translator enters a Finnish answer. The configured AI provider evaluates meaning and Finnish quality, then the server calculates that round's score. Feedback compares the original and corrected sentences word by word: unchanged words and corrections are green, incorrect original words are red and struck through, and added or removed words are marked. Punctuation and spacing remain visible and unchanged. Feedback also shows each justified change and reason, an explanation, short point-form Finnish grammar rules when relevant, and the score breakdown before the translator advances.

Everyone in an active game sees whose turn it is. The name comes from the latest lobby roster; the player sees “(you)” on their own turn and “(reconnecting)” while the current translator is offline. A reconnect snapshot restores the active round and translator. Stale round events cannot move the displayed turn backward.

## Scoring

Each evaluated answer receives a meaning grade from 0 to 7 and a Finnish quality grade from 0 to 3. The server validates the two grades and adds them for a maximum of 10 points per round. Meaning points reflect how much of the English meaning the Finnish answer preserves: 7 means all of it, 5–6 means most of it, 2–4 means part of it, and 0–1 means little or none. Finnish quality reflects naturalness and grammar: 3 is natural, 2 has minor errors, 1 is understandable with several errors, and 0 is unusable.

The `isCorrect` result means that the answer preserves all of the English meaning (7/7). Finnish mistakes can still be corrected and lower the Finnish grade while the meaning remains correct. Valid alternative translations are accepted. A partly accurate answer earns the points assigned to both dimensions. Revealing a hint or the full reference answer awards or removes no points. Only the translator earns the score for that round.

The server persists the submitted answer before evaluation, and persists its accepted evaluation with the updated group score. If provider evaluation fails, the answer remains available and the same round can be retried. The next question stays blocked until evaluation succeeds. Repeating a successful check returns the saved grade and does not award points twice.

## Hints and answer visibility

The translator sees English words in a table and can reveal each Finnish word separately. Unrevealed Finnish values are omitted from the translator's browser payload; the server returns one only after an authorized reveal. Reconnect restores already revealed words. Other players can reveal the full reference answer, which is sent to observers and never to the translator. Repeated reveal actions do not change scores.

## Server credential and provider reliability

Players do not enter API keys. Every generation and evaluation prompt enters a bounded in-memory FIFO with one consumer, so one API process sends one game AI request at a time. Gemini is the only game provider; OpenAI credentials are not loaded or sent to the game API. Numbered GitHub secrets `GEMINI_KEY_1` through `GEMINI_KEY_10` and supported legacy Gemini secrets are passed to the API host and stored in its permission-restricted `.env`. Empty slots are ignored. Keys never enter browser configuration, game records, or diagnostic messages.

Gemini quotas are shared by Google Cloud project and model, not by API key. Extra keys from the same project do not add RPM or RPD. The coordinator reserves requests against a database-backed rolling 24-hour budget (default 15) and spaces requests at the configured RPM (default 3). GitHub repository variables `GAME_GEMINI_RPM` and `GAME_GEMINI_RPD` can lower those limits; raise them only after checking the exact model/project quota in AI Studio. The rolling 24-hour application budget is intentionally conservative and does not align exactly with Google's RPD reset at midnight Pacific. The reservation uses a read/modify/write document and is safe for the current single API instance. Do not scale the API horizontally until quota reservation is changed to an atomic shared operation.

Gemini errors are classified from HTTP status and structured error fields. Explicit daily quota failures do not retry immediately and cool down through the next midnight Pacific; other quota errors with no reliable reset use a conservative cooldown and tell the player to retry later. A rate-limit 429 or service failure retries at most once with bounded exponential backoff and jitter, honoring `Retry-After` only when it fits the request time budget. An ambiguous 429 is treated as a shared model throttle. Same-project keys are not rotated for quota, project access, invalid requests, or service failures; another configured key is tried only when Gemini explicitly identifies a key-specific credential error. Sanitized status, error code, reason, and provider message are available in the existing expandable error detail; credentials and raw provider bodies are never shown.

There is no runtime sample fallback and no unverified local or player grading. If generation or evaluation still fails, the current round remains and the player can retry; a submitted answer is retained and the game cannot advance until evaluation succeeds. Routine CI uses deterministic provider fakes and does not consume owner quota. The manually started `game-ai-live-smoke` workflow always exercises both generation and evaluation against Gemini only.

See [Gemini rate limits](https://ai.google.dev/gemini-api/docs/rate-limits), [Gemini troubleshooting](https://ai.google.dev/gemini-api/docs/troubleshooting), and [Gemini API errors](https://ai.google.dev/gemini-api/docs/api-errors) for Google's current quota and error guidance.

## Test coverage

`EntertainmentAiQueueTests` covers FIFO behavior, Gemini-only selection, shared cooldown, bounded transient retry, credential-specific key rotation, and the local request budget. `GeminiProviderErrorParserTests` covers structured 429/403 classification and credential redaction. The deployment script tests require Gemini credentials and remove retired OpenAI game secrets. `GeminiAnswerEvaluatorTests` covers alternate answers, grammar corrections with preserved meaning, incorrect answers and invalid provider output. `EntertainmentHubIntegrationTests` exercises real SignalR identity, reconnect, retry, score persistence and idempotent transitions. `site/e2e/entertainment-game.spec.ts` checks the visible score breakdown alongside feedback and retry behavior. PostgreSQL reservation tests cover history uniqueness and concurrency in CI.

The previous sample-catalogue behavior is retired. `game-sample-fallback.md` is superseded by this page.

## Retry quota accounting

Every internal Gemini HTTP retry reserves another request against the same model/project RPM and rolling 24-hour budget as the first attempt. A transient failure or transport error cannot bypass spacing or spend a second request after the daily budget is exhausted. Both question generation and answer evaluation use this path. Budget exhaustion keeps the existing retry-only flow and reports the configured budget reset rather than claiming that the provider returned invalid content. Tests use synthetic HTTP handlers and do not call Gemini.

### In-memory room retention

Game state and player credentials remain in durable storage. The process-local room registry admits at most 1000 rooms atomically across both action leases and connection registration. Active holders, queued acquisitions and connected players keep their room alive. After the last reference leaves, an empty room stays available for a 10-minute reconnect grace period, then the next admission sweeps it. Reconnecting after expiry reloads the existing durable game; it does not create a new round or reset scores.

Configure the grace period with `Entertainment:Rooms:IdleRetention` (for example `00:10:00`, or environment variable `Entertainment__Rooms__IdleRetention`). Nonnegative values are accepted, including zero for immediate idle reclamation. Retained idle rooms count toward the cap until their grace period expires. The existing one-second action wait and player token checks remain in effect. Disconnect/replacement clears that connection's action-rate queue; an obsolete connection cannot recreate a queue.
