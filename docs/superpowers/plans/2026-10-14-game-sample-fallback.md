# Language-game fallback implementation

1. Add a read-only12-sample-per-level catalogue with English/Finnish/hints,
   recent-history exclusion and bounded recycling; mark samples internally.
2. Bound Gemini generation to one configured1–30 second attempt (default8).
   Use samples for provider/network/timeout/invalid-output failures, with confined
   parse handling and secret-free warnings. Propagate caller cancellation.
3. Preserve successful provider duplicate retries; skip redundant retries for
   samples. Check cancellation at handler entry and before game mutation/saves.
4. Test all levels, output/error/cancellation boundaries and a complete outage
   game without credentials. Retain completion assertions for the optional live
   probe; remove its old swallow-and-pass behavior. Document and run full CI.
5. Merge this fix, then rebase/reverify report PR104 onto updated main and merge
   it. Verify deployment state accurately; do not claim unbuilt changes are live.

The brainstorming spec reviewer approved the design after precise provider
validation and handler no-write cancellation requirements were added. No
writing-plans skill is available, so the plan follows repository conventions.
