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

Players do not enter API keys. Every game generation and answer evaluation prompt enters a bounded in-memory FIFO with one consumer, so the API processes one AI prompt at a time. The server tries configured Gemini credentials, then OpenAI credentials when needed. Numbered GitHub secrets are `GEMINI_KEY_1` through `GEMINI_KEY_10` and `OPENAI_API_KEY_1` through `OPENAI_API_KEY_10`; empty slots are ignored, and only configured keys enter the pool. Gemini legacy secrets remain supported. An unnumbered `OPENAI_API_KEY` is also supported and is used as OpenAI slot 1 when `OPENAI_API_KEY_1` is empty. Keys are stored only in the API host's permission-restricted `.env` and passed to the container at deploy; they never enter browser configuration, game records, logs, or user input. OpenAI uses `GAME_OPENAI_MODEL` when configured and defaults to `gpt-4.1-mini`.

Provider quotas are shared across keys for the same provider/model, so adding keys does not add RPM or RPD. The coordinator smooths requests to at most one request per configured interval and enforces a rolling 24-hour daily budget. Usage timestamps are stored in the existing database so API restarts do not reset that budget. Defaults are Gemini 3 RPM / 15 requests per 24 hours and OpenAI 5 RPM / 50 requests per 24 hours. Set GitHub repository variables `GAME_GEMINI_RPM`, `GAME_GEMINI_RPD`, `GAME_OPENAI_RPM`, and `GAME_OPENAI_RPD` to lower values if the account dashboard requires it; set higher values only after checking the exact model/account quotas. The current OpenAI response rate-limit headers are recorded in API logs when the provider returns them. A provider HTTP 429 cools down that provider/model and skips its other keys, honoring `Retry-After` when present, then lets the other provider try. This avoids burning additional calls against a shared quota.

There is no runtime sample fallback and no unverified local or player grading. Provider requests have bounded timeouts. If generation or evaluation still fails, the game keeps its current round and allows retry. Routine CI uses deterministic provider fakes and does not consume owner keys. The manually started `game-ai-live-smoke` workflow exercises generation and evaluation with a selected server provider (`openai`, `gemini`, or `both`) and prints observed OpenAI request-limit headers when returned.

Google applies Gemini limits by project, and the current tier/model limits are shown in AI Studio. OpenAI limits vary by organization, project, model and usage tier; 429 responses can also mean a billing or spend limit rather than RPM. See [Gemini rate limits](https://ai.google.dev/gemini-api/docs/rate-limits) and [OpenAI guidance for rate limits and 429 errors](https://help.openai.com/en/articles/5955604-troubleshooting-api-rate-limits-and-429-errors).

Adding several keys from the same Google Cloud project does not create separate quota pools; Gemini limits apply at the project level. Rotation can move to a different provider or independently limited credential, but it does not multiply a shared project's quota.

## Test coverage

`api/CleaningSuite.Tests/Entertainment/EntertainmentScoringTests.cs` checks score bounds, totals and inconsistent grades. `GeminiAnswerEvaluatorTests` covers alternate answers, grammar corrections with preserved meaning, incorrect answers and invalid provider output. `EntertainmentHubIntegrationTests` exercises real SignalR identity, reconnect, retry, score persistence and idempotent transitions. `site/e2e/entertainment-game.spec.ts` checks the visible score breakdown alongside feedback and retry behavior. PostgreSQL reservation tests cover history uniqueness and concurrency in CI.

The previous sample-catalogue behavior is retired. `game-sample-fallback.md` is superseded by this page.
