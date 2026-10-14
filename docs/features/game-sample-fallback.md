# Language-game sample fallback

Issue105 keeps game start and later rounds usable when Gemini generation is
unavailable. The provider is attempted once; valid English/Finnish sentences and
Finnish word hints are retained. HTTP failures (including invalid key/quota/server
responses), network failures, provider timeouts and invalid provider output use
sample content instead.

`Entertainment:Gemini:TimeoutSeconds` defaults to8 and is clamped to1–30 seconds.
Environment equivalent: `Entertainment__Gemini__TimeoutSeconds`. The deadline
covers request and response reading. The service does not add vendor retries
before fallback; a later round can try Gemini again and recover normally.

The catalogue contains12 predefined sentences for each CEFR level A1–C2, with
Finnish translations and dictionary/base-form hints. These are generic daily-life
and work exercises; they are not customized to the group description. Selection
excludes the group's most recent ten sentences, comparing trimmed English text
without case. If an unusually larger supplied history contains every sample, the
least recent sample is reused. Older samples can cycle in long games.

Samples are tagged only within the sentence-service result. Public round DTOs and
stored entities are unchanged. Game handlers avoid repeated provider generation
for a known sample; valid Gemini duplicate-generation retries remain in place.
Existing creator/key input, encrypted storage and game authorization are retained.
No new sample-mode UI or tenant/role changes are introduced.

Provider-output validation requires nonblank English and Finnish text and at least
one nonblank Finnish hint. Existing string hints and `{ "fi": "..." }` objects are
accepted. Missing fields, wrong JSON types, blank hints and corrupt provider JSON
trigger fallback. Parsing exceptions are handled only while parsing provider data;
unexpected programming, key-decryption and repository failures remain errors.

Caller cancellation is distinct from provider timeout. Pre-canceled requests and
cancellation during generation propagate without returning a sample or mutating/
saving game state. Cancellation checks run at service/handler entry, before returning
content, and before handler mutation/saves. Persistence semantics once a save has
begun are unchanged. Warnings contain only failure category/HTTP status and level,
never the API key, group description or provider response body.

`GeminiFallbackTests` covers all supported levels, recent-history/cycling behavior,
successful provider output, HTTP/network/deadline failures, malformed/blank/wrong
output and cancellation through real handlers, including a provider that ignores
cancellation. The deterministic full-game outage test exercises real create/join/
start/rotation/end handlers without external credentials or round sleeps.
The optional `GEMINI_KEY` live probe still asserts complete-game outcomes through
the same runtime fallback; it no longer turns a caught upstream error into a pass.
