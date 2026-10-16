# Implementation plan: game grammar feedback and turn status

## Goal

Implement the approved design in `docs/superpowers/specs/2026-10-16-game-grammar-turn-status-design.md`: concise Finnish grammar-rule bullets, a deterministic accessible word-level answer comparison, and a current-turn label for all players.

Additional approved requirement during implementation: use conservative configurable RPM/RPD limits across all keys for each provider/model, persist rolling 24-hour usage in Marten, and directly smoke-test the configured OpenAI key without routing the live request through Gemini.

## Tasks

1. **Extend the evaluation contract.** Add a required `GrammarRules` list to `EntertainmentAnswerEvaluation`, update the shared Gemini/OpenAI structured-output schema and evaluator instructions, and validate array shape, item count, Unicode scalar length, empty values, and control characters. Keep meaning and Finnish scoring unchanged. Add evaluator tests for empty/valid values, invalid boundaries, and the `Helsingillä` → `Helsingissä` contrast fixture.

2. **Add deterministic word diff.** Create a pure site helper that tokenizes Unicode word spans and separator spans, computes an LCS with the approved original-first tie-break, pairs adjacent deleted/inserted runs as replacements, and returns unchanged/incorrect/corrected/added/removed segments while preserving exact sentence text. Add focused tests for substitutions, repeated words, insertion/deletion, punctuation, spacing, and Finnish diacritics.

3. **Render answer feedback.** Update the API TypeScript type and game UI to show labeled original/corrected sentences with accessible green/red spans and a point-form Grammar rules section only when rules exist. Keep the overall explanation and score display; do not change scoring.

4. **Expose turn status.** Resolve `translatorPlayerId` against the latest lobby roster. Show the translator's name and online/reconnecting state during active rounds. Ignore older or conflicting same-number round events, preserve state for duplicate same-round snapshots, and update only when the next round starts. Add integration and Playwright coverage for observers, evaluation retry, reconnect, and stale events.

5. **Bound provider quotas.** Smooth provider requests using configured RPM, enforce rolling 24-hour RPD across every key for a provider/model, and persist request timestamps in the system entertainment tenant. On HTTP 429, cool down the provider/model and skip its remaining keys. Add focused tests for minute pacing, daily budget and avoiding repeated same-provider calls after 429; wire conservative defaults through deployment settings.

6. **Document and verify.** Update `docs/features/language-game.md`; add provider-specific live smoke selection and log returned OpenAI rate-limit headers. Run backend build/tests, site build, relevant Playwright tests and the full Playwright suite. Review the final diff, commit, open a PR, merge after CI/review pass, and confirm production deployment.

## Constraints

- No additional AI call for grammar rules; the rule list travels with the existing evaluation document and SignalR payload. The quota ledger adds one Marten document type in the fixed entertainment tenant and its schema is created by the existing startup schema initializer.
- Invalid provider output follows the existing retry-only path and never saves partial evaluation feedback.
- No turn reassignment or scoring changes.
