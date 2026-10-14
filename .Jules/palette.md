## 2026-10-06 - Localized ARIA Labels and Focus States on Navigation Controls
**Learning:** Hardcoded English ARIA labels on multi-lingual controls degrade accessibility for screen-reader users operating in Finnish or Swedish.
**Action:** Always derive `aria-label` dynamically based on the active language (`lang`) or translation dictionary for header controls.

## 2026-10-14 - Screen Reader Support for Visually Hidden Text Reveal Controls
**Learning:** Using `text-transparent` to visually hide unrevealed/spoiler text causes screen readers to read the hidden text out loud upon focus, spoiling the content before user interaction.
**Action:** Wrap visually transparent text in `<span aria-hidden="true">` and set an explicit `aria-label` (e.g., "Reveal hint word") on the parent button.
