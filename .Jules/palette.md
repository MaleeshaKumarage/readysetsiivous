## 2026-10-06 - Localized ARIA Labels and Focus States on Navigation Controls
**Learning:** Hardcoded English ARIA labels on multi-lingual controls degrade accessibility for screen-reader users operating in Finnish or Swedish.
**Action:** Always derive `aria-label` dynamically based on the active language (`lang`) or translation dictionary for header controls.
