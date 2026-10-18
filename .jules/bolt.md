## 2025-05-20 - Map caching vs Flat Object Lookups for i18n
**Learning:** In V8 / Node / JS engines, dynamically building string keys (`lang + ':' + path`) to index into a runtime `Map` cache for translation path lookups introduces higher overhead (string concatenation + Map lookup hash calculation) than simple array splitting and property access, and is ~10x slower than pre-flattening nested objects into a plain flat JS object lookup table.
**Action:** For static JSON dictionaries or configuration trees, pre-flatten all nested keys into a flat plain object `{ 'nav.services': 'Services' }` at module load time for O(1) property access.

## 2026-03-29 - TimeZoneInfo.ConvertTimeToUtc vs Fixed Offset Span in Loops
**Learning:** Calling `TimeZoneInfo.ConvertTimeToUtc` inside a tight loop (e.g. 24–48 slot evaluations per availability request) introduces noticeable CPU overhead due to repeated daylight saving rule lookups and DateTime allocations. Comparing the UTC offsets at the start and end of a time window allows bypassing `TimeZoneInfo` conversions via direct arithmetic when offset is uniform.
**Action:** When computing UTC intervals over a daily wall-clock window, verify if the UTC offset is uniform across window boundaries and use direct offset arithmetic for the fast path, falling back to `TimeZoneInfo.ConvertTimeToUtc` if offsets differ.
