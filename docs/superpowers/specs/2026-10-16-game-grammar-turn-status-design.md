# Game grammar feedback and turn status

## Goal

Make each answer more useful for Finnish learners and make the active turn obvious to everyone in the game. Keep the existing round, scoring, correction, and retry flow.

## Current behavior

The evaluator returns a corrected answer, an overall explanation, and per-change correction reasons. The server persists and broadcasts the same evaluation to the group. The active translator is already identified by `translatorPlayerId` on each `roundStarted` event, including reconnect snapshots. The game UI changes its instructions depending on whether the local player is the translator, but observers do not see the translator's name as a clear turn indicator.

## Answer feedback

Extend `EntertainmentAnswerEvaluation` with a required `grammarRules` list of concise learner-facing rules. The list may be empty and is limited to five strings. Each string must contain 1–200 Unicode scalar values, with no control characters. The evaluator prompt requests rules that explain Finnish grammar issues actually present in the submitted answer and complement, rather than repeat, inline correction reasons. Each point uses plain English and may include a short Finnish example. When useful, it contrasts the corrected form with the learner's form and explains why the chosen case or ending fits the sentence. For example: “Use *Helsingissä* (-ssä, ‘in Helsinki’); *Helsingillä* (-llä, ‘on/at Helsinki’) does not express being in the city here.” If there are no useful grammar rules, return an empty list.

The provider schema requires the array and constrains its item count and lengths. The parser also checks the array type, non-empty string items, 200-Unicode-scalar limit, and absence of control characters. A missing, malformed, over-count, or over-length field makes the entire provider response invalid and uses the existing retry flow; do not persist or broadcast partial feedback. Semantic relevance is a prompt requirement, not a deterministic parser guarantee; automated tests verify the prompt/shape and known examples, not an impossible general-purpose proof that a natural-language rule is relevant. Grammar rules do not affect correctness, either score dimension, or turn ownership.

Show a **Grammar rules** section as a simple unordered list beneath the correction comparison. Omit the section when `grammarRules` is empty. Keep the existing overall explanation focused on meaning; grammar-rule bullets explain Finnish form choices and avoid repeating per-change reasons. The translator and observers receive the same list through the existing persisted evaluation and `answerEvaluated` event; Marten stores the added document field with the evaluation, and reconnect snapshots restore it without a new endpoint, event, or database migration.

### Word-level color comparison

Display the submitted and corrected sentences as aligned word-level comparisons. Tokenize each sentence into Unicode word spans (`\p{L}`, `\p{M}`, and `\p{N}` sequences, allowing an internal straight or curly apostrophe) and intervening separator spans. Compare word spans exactly, including Finnish diacritics and case, with a longest-common-subsequence alignment. When equally long alignments are possible, advance the original-answer index first; this is the deterministic tie-break for repeated words. Pair adjacent runs of deleted and inserted words in order as replacements; show remaining deleted words as removals and remaining inserted words as additions. Punctuation and whitespace remain in the original/corrected text and are never color-marked by this word-level diff.

Mark unchanged word spans green in both answers, incorrect/original spans red with strike-through, corrected replacement spans green, additions green, and removals red with strike-through. Thus, in a five-word answer with one wrong form, the four unchanged words remain marked as correct and only the changed form is marked incorrect/corrected. Keep “Original answer” and “Corrected answer” headings. Each colored word span has an accessible label (“Unchanged word: …”, “Incorrect original word: …”, “Corrected word: …”, “Added word: …”, or “Removed word: …”); color is supplementary, not the only cue. The displayed sentences preserve each input's exact word order, punctuation, and whitespace.

## Turn status

On every active game round, show a visible status such as **“Current turn: Alice”** to all connected players. Resolve the existing `translatorPlayerId` against the latest `lobbyUpdated` roster, which is the source of player names and `IsOnline`; if no name is currently available, show **“Current turn”** rather than a stale name. If the translator is offline, show “(reconnecting)” beside their name. Hide the status in lobby and results views.

Accept a round event as current when no round is displayed or its `roundNumber` is greater than the displayed round. Ignore older round events. For a duplicate snapshot of the same `roundId`, refresh answer/evaluation state but retain that round's `translatorPlayerId`. A different `roundId` with the same `roundNumber` is stale/invalid and must not replace the active round. This prevents a delayed event from reverting the turn indicator.

The current translator continues to own the turn while typing, while an AI check is running or being retried, and after feedback is shown until the next round starts. During next-round generation, retain the previous round's label until the new `roundStarted` event arrives; then update it from the new round's translator ID.

If the current translator is offline, keep their identity visible and indicate that they are reconnecting/offline using the lobby's online state. Preserve the existing rule that only connected players are selected for the next turn. Do not infer the next translator from client-side score, roster order, or the local player's role.

## Error and state behavior

If AI evaluation fails, keep the answer and current-turn label unchanged; show retry as today. Do not show grammar bullets until a valid evaluation arrives. A duplicate evaluation must not duplicate feedback or award points twice. A reconnect snapshot restores the current translator and full saved evaluation. During next-round generation, retain the previous round's translator label until the new round starts.

## Verification

- Evaluator tests accept empty and valid grammar-rule lists, and reject missing/malformed arrays, non-string/empty items, control characters, more than five items, and items exceeding 200 Unicode scalar values. A fixture for `Helsingillä` corrected to `Helsingissä` verifies the prompt/output includes a point-form case contrast.
- Existing answer scoring and correction tests confirm grammar rules do not change points or correctness.
- Hub integration tests verify the persisted evaluation including grammar rules is broadcast and restored on reconnect, and that the translator ID remains unchanged during evaluation retry.
- Word-diff unit/Playwright fixtures assert exact segments for a one-word substitution (`Minä asun Helsingilla.` → `Minä asun Helsingissä.`), a repeated word with an ambiguous alignment, an insertion and deletion, punctuation-only differences, and Finnish diacritics. Assert unchanged words remain green, changed/add/remove spans receive the specified style and accessible label, and each sentence's text/order/spacing is preserved. Grammar rules render as a point list only when present.
- Playwright and hub tests verify observers see the current translator, the label stays through evaluation retry, changes when the next round starts, ignores older/different same-number events, and restores from a reconnect snapshot; offline state comes from the latest lobby roster.
- Run backend build/tests, site build, and the full Playwright suite before merge. Exercise the approved production flow after merge through normal CI/deploy.

## Scope exclusions

This change does not add grammar lessons, a separate language model call, rule browsing/history, new scoring, turn reassignment controls, or a new API endpoint. It does not change the existing translation prompt or hide the answer/correction from observers.

## Quota-safety addendum (approved during implementation)

All AI calls continue through the existing bounded single-consumer queue. Request pacing and a rolling 24-hour request budget apply to the provider/model pool across every configured key, since key rotation does not increase a shared account/project quota. Defaults are Gemini 3 requests/minute and 15 requests/24 hours, and OpenAI 5 requests/minute and 50 requests/24 hours; deployment can override these with repository variables after checking the actual account/model limits. Persist usage timestamps in the fixed entertainment tenant so API restarts do not reset the daily budget. On HTTP 429, apply a provider/model-wide cooldown using `Retry-After` when present, skip other keys for that provider, and continue to the other provider. The live smoke workflow must allow OpenAI-only selection and log OpenAI rate-limit response headers when returned. No live credentials or key values are shown in logs.
