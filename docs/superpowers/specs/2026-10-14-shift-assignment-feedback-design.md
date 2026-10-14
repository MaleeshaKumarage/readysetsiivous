# Visible shift assignment failures

The API already rejects overlapping recurring shifts with HTTP 409 and problem
detail. The admin client reduces failures to false, and the assignment page ignores
that result, clears the selection and refreshes as if assignment succeeded.

Use the user's permitted notification approach: preserve server-side scheduling
validation and show employee-specific error messages. Do not duplicate recurring
schedule calculations or claim employees are available before the server checks.
An availability endpoint and disabled conflict choices would require a shared
policy and additional reads; that is unnecessary for correcting silent failures.

Make only shift assignment client operations throw actionable errors, preserving
other admin client contracts. Read assignments fresh without the generic GET cache.
Use problem details for client errors and safe fallback messages for network,
malformed responses and server errors. Failed list reads must not imply an empty
roster. Both assignment and removal errors must be visible with retry available.

In the admin picker exclude inactive and already assigned employees. Lock mutations
while pending. For multi-selection process each employee, track success and failure,
refresh the roster and retain failed choices. Explain partial success without
implying a batch rollback. If a network failure actually saved an assignment, a
successful refresh reconciles that employee out of the selection; retain the
uncertain-operation message. Guard late assignment-list responses when switching
or closing rows so they cannot overwrite the current selection/error/loading state.
While a mutation is pending, prevent row switching/closing and shift deactivation;
use a synchronous pending ref to prevent repeated clicks before React renders.
Apply generation checks to mutation refresh and final selection/error/loading
updates too. Test delayed mutations and disabled navigation until completion.

Managers already receive problem-detail errors and retain selection on failure;
verify their overlapping-shift notification and filter inactive employees too.
No role/schema/API endpoint or overlap policy change. Existing 180-day conflict
window remains unchanged and is documented as a separate scheduling limitation.

Playwright desktop/mobile tests cover conflict detail, non-JSON/server/network
errors, retry, partial success, stale responses, double-click suppression, inactive
and duplicate filtering, list failure recovery and removal failure. Existing backend
overlap tests continue to verify rejection without persistence. Merge passing CI
and verify deployed assignment UI after website deployment.
