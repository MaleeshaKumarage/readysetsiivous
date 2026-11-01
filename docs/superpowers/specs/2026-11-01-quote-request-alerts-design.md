# Quote request alerts

Approved in chat, including the user's red tag preference.

Admins see a persistent banner on every authorized admin page when active branches
have outstanding quote requests. It shows the pending branch count and a Review
quotes link opening Partner operations directly on its Quote requests tab.
Every pending branch shows a red, text-labelled `Quote requested` badge in
Companies, Partner properties, the quote queue and the owner's property view.
Use the existing light theme and readable red foreground/background contrast.

Reuse the existing tenant-scoped admin quote queue and branch DTO fields. Pricing
readiness resolves a request even if its historical request timestamp remains.
Refresh on navigation, window focus, visibility return and every 30 seconds while
visible. Stop requests when unmounted/unauthorized; ignore stale responses.
Refresh immediately after saving pricing, including Companies branch editing.
An API error must not masquerade as zero requests; provide a Retry action and
retain the last successful queue. Background refresh must not reset open forms.

Do not add websocket infrastructure, emails, new database fields or change
authorization. Managers retain the same red property badges in their existing
Partner operations view; the tenant-wide banner is admin-only as requested.

Verification covers new requests appearing without reload, direct quote-queue
navigation, red/text badges, pricing clearing badge/banner, failed reads/retry,
multiple counts, hidden tabs and unauthorized users making no quote-queue reads.
Run focused browser tests on desktop/mobile plus TypeScript and production build.
