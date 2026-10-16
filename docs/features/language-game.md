# Language game

The realtime language game generates a new English sentence and Finnish word hints for each group round. The active translator enters a Finnish answer. Gemini evaluates meaning and Finnish quality, then the server calculates that round's score. Feedback shows the submitted answer, corrected answer, each justified change and reason, an explanation, and the score breakdown before the translator advances.

## Scoring

Each evaluated answer receives a meaning grade from 0 to 7 and a Finnish quality grade from 0 to 3. The server validates the two grades and adds them for a maximum of 10 points per round. Meaning points reflect how much of the English meaning the Finnish answer preserves: 7 means all of it, 5–6 means most of it, 2–4 means part of it, and 0–1 means little or none. Finnish quality reflects naturalness and grammar: 3 is natural, 2 has minor errors, 1 is understandable with several errors, and 0 is unusable.

The `isCorrect` result means that the answer preserves all of the English meaning (7/7). Finnish mistakes can still be corrected and lower the Finnish grade while the meaning remains correct. Valid alternative translations are accepted. A partly accurate answer earns the points assigned to both dimensions. Revealing a hint or the full reference answer awards or removes no points. Only the translator earns the score for that round.

The server persists the submitted answer before evaluation, and persists its accepted evaluation with the updated group score. If provider evaluation fails, the answer remains available and the same round can be retried. The next question stays blocked until evaluation succeeds. Repeating a successful check returns the saved grade and does not award points twice.

## Hints and answer visibility

The translator sees English words in a table and can reveal each Finnish word separately. Unrevealed Finnish values are omitted from the translator's browser payload; the server returns one only after an authorized reveal. Reconnect restores already revealed words. Other players can reveal the full reference answer, which is sent to observers and never to the translator. Repeated reveal actions do not change scores.

## Server credential and provider reliability

Players do not enter API keys. The API reads `Entertainment:Gemini:ApiKey`, supplied in deployments as `Entertainment__Gemini__ApiKey`. The production deployment uses GitHub Actions secret `GEMINI_KEY_2`, with `GEMINI_KEY` and `GEMINI_API_KEY` as compatibility fallbacks. The deployment script stores it only in the API host's permission-restricted `.env`; it never writes the key to the repository, browser configuration, game group or logs. The checked-in production and dev Compose files pass that variable into the API container.

Generation and evaluation both require Gemini. There is no runtime sample fallback and no unverified local or player grading. Provider requests share a process-wide queue and use bounded timeouts and retry for transient failures. If generation or evaluation still fails, the game keeps its current round and allows retry. Routine CI uses deterministic provider fakes and does not consume the owner key. The manually started `game-ai-live-smoke` workflow exercises one generation call and one evaluation call with the server secret.

Adding several keys from the same Google Cloud project does not create separate quota pools; Gemini limits apply at the project level. The service therefore uses one configured owner key and controls concurrency/retries instead of rotating among same-project keys.

## Test coverage

`api/CleaningSuite.Tests/Entertainment/EntertainmentScoringTests.cs` checks score bounds, totals and inconsistent grades. `GeminiAnswerEvaluatorTests` covers alternate answers, grammar corrections with preserved meaning, incorrect answers and invalid provider output. `EntertainmentHubIntegrationTests` exercises real SignalR identity, reconnect, retry, score persistence and idempotent transitions. `site/e2e/entertainment-game.spec.ts` checks the visible score breakdown alongside feedback and retry behavior. PostgreSQL reservation tests cover history uniqueness and concurrency in CI.

The previous sample-catalogue behavior is retired. `game-sample-fallback.md` is superseded by this page.
