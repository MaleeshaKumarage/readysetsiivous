# Partner workflow UI consistency

Approved in chat: match each workflow to its surrounding UI; keep actions,
permissions, API payloads and language behavior unchanged.

Admin Partner requests uses existing Mantine light components and admin theme:
Title, Button, Paper, Input, NativeSelect and Textarea. Keep existing accessible
names, tabs, red quote badges, confirmation/retry/draft behavior and navigation.
Use wrapped responsive actions and fields with visible focus. The shared manager
operations view must remain usable, with a local Mantine provider matching the
same light presentation when no admin provider exists.

Cleaner acceptance uses the same shared Card, Button, Field and Input as My shifts.
Honor the parent light/dark theme, with readable offer details, error/loading
feedback and wrapping actions. Retain planned-window validation and explicit
accept/decline retry behavior. No owner-portal redesign or translation changes.

Validate admin/manager pricing, property changes, offers and navigation plus cleaner
accept/decline, failure recovery and planned dates. Check desktop/mobile and dark
cleaner contrast; inspect rendered screenshots and run the production build.

Implementation: replace mismatched primitives in the two shared workflow components,
add narrowly scoped layout styling, extend existing workflow tests with visual/theme
assertions, then build and open a PR. Keep backend and release policy unchanged.

Approved follow-up: menu badge totals pending Requested cleaning work, active unpriced quotes and unresolved changes. Each relevant tab shows its pending count. Offered/accepted/completed/cancelled work does not increase the pending-work count. Hide cancelled requests without deleting history. Refresh counts using the existing visibility/focus/navigation/poll lifecycle, and after actions; denied users never mount it.
