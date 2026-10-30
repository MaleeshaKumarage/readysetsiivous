## 2026-10-06 - Localized ARIA Labels and Focus States on Navigation Controls
**Learning:** Hardcoded English ARIA labels on multi-lingual controls degrade accessibility for screen-reader users operating in Finnish or Swedish.
**Action:** Always derive `aria-label` dynamically based on the active language (`lang`) or translation dictionary for header controls.

## 2026-10-06 - ARIA Tablist Container Child Element Constraints
**Learning:** Placing non-tab action controls (such as a Clear button) directly inside a `role="tablist"` element violates WAI-ARIA structural rules and breaks screen reader navigation.
**Action:** Wrap only `role="tab"` buttons inside `role="tablist"`, keeping action buttons adjacent in a parent flex wrapper.

## 2026-10-30 - Custom Dropdown Menu Keyboard Navigation & ARIA Patterns
**Learning:** Custom dropdown menus without WAI-ARIA menu roles (`role="menu"`, `role="menuitemradio"`) and arrow key handlers lock keyboard/screen reader users into standard tab order instead of allowing menu traversal.
**Action:** Add `aria-haspopup="menu"`, `role="menu"`, `role="menuitemradio"`, auto-focus on open, and `ArrowUp`/`ArrowDown`/`Home`/`End` focus management to custom dropdown popups.
