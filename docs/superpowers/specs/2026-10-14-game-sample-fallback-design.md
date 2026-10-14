# Language-game sample fallback

The user requests fallback to sample inputs when real Gemini generation is
unavailable. Keep successful Gemini output in use; avoid blocking game start or
subsequent rounds on upstream failures. This is a separate change from issue102.

## Contract

The existing sentence service returns English text, Finnish translation and
Finnish dictionary/base-form hints. A curated catalogue supplies all three at
each supported CEFR level (A1 through C2). Provide12 samples per level so the
normal ten-sentence recent-history window always has an unused alternative.
Choose a sample absent from recent history, comparing trimmed English text without
case; if an unusually larger history exhausts the catalogue, choose its least
recent matching sample. Samples use generic everyday/work practice contexts and
do not pretend to match arbitrary group descriptions.

Try Gemini once with a bounded request timeout (default8 seconds, configurable
from1 to30). Successful valid output retains existing response fields. On HTTP
failure, network error, internal timeout, malformed/empty provider JSON or missing
translation/hints, return a level-matched sample. Caller cancellation remains
cancellation and must never become a fallback or trigger a persistence write.
Unknown levels and programming/key-decryption/repository errors remain errors.

Mark fallback sentences internally, without changing public round DTOs or DB
entities. Game handlers skip their provider duplicate-generation retries for a
known sample; recent history drives sample selection and older samples may cycle.
Existing duplicate retries for successful Gemini sentences remain unchanged.
Preserve existing group API-key input/encryption and game authorization/lifecycle
rules; no new sample-mode UI or role changes are needed.

Log a generic warning for fallback with level/failure category. Do not log API
keys, request bodies, descriptions or full provider response bodies. Do not catch
all exceptions or hide persistence failures.

## Verification

Fake HTTP tests cover success unchanged, HTTP401/429/5xx, network failure,
provider timeout, caller cancellation, malformed/empty JSON and missing fields.
Catalogue tests cover every level, valid translations/base forms, recent-history
avoidance, exhaustion behavior and invalid levels. Drive a complete game through
real handlers with a failing HTTP provider, checking start, translator rotation,
distinct samples and final standings without external credentials or sleeps.
The optional live full-game probe still asserts completion through the same
service; remove its old exception-swallowing path since availability is now a
runtime behavior under test. Update feature docs and CI evidence, merge this PR
before rebasing the report fix so its external-service timeout no longer blocks
release. No frontend behavior/schema changes are required.
