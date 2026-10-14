# Active and Inactive management tabs

## Behavior and scope

Use a shared accessible Active/Inactive tab control on admin agreements, shifts, employees, services, companies, expanded company branches and checklist templates, and the manager shift list. Active is the initial selection. Tabs filter solely on `isActive`; agreement signing status and shift occurrence completion do not determine lifecycle. Signed and partially signed agreements remain intact and viewable in the matching lifecycle tab. No cleanup, deletion, new lifecycle endpoint or permission changes are included.

Keep each list's current create/edit/view/download/assignment/reactivation actions and permissions. Deactivation removes a record from Active and reveals it in Inactive after reload; activation does the reverse. Creating an active record returns its list to Active so the new record is visible. Switching tabs clears expanded shift assignment views where needed to prevent hidden selection/actions. Manager employee selectors remain limited to active assignable employees, and historical reporting continues to use its existing filters.

List APIs already support all lifecycle records. Employees must explicitly request includeInactive=true; shifts and manager shifts should load includeInactive=true then filter locally, replacing the mixed-list checkbox. Services already load all records; agreements and templates already return both. Companies are paged: fetch all pages with the existing API pagination contract for this management list, reject incomplete/failed pagination, and keep branch details tenant-scoped. Use separate branch tab state per expanded company (or reset to Active on company expansion) and no inactive-only assignment options.

Show a helpful empty state for the selected lifecycle when the other tab contains records; keep existing first-use empty messages for genuinely empty datasets. Counts, when shown, reflect the full successfully loaded list. Tabs are keyboard accessible and usable on mobile. No load occurs merely because a local status tab changes, avoiding stale response races.

## Alternatives

Recommend one shared tab component plus client filtering over the existing all-record endpoints: consistent UX with no schema/API migration. Independent per-page tab implementations duplicate accessibility and lifecycle rules. Server status filters would improve very large list scalability but require new contracts across otherwise unpaged lists; leave that separate from this UI request. Existing company pagination must still be consumed completely so records after the first100 are not silently omitted.

## Validation and delivery

Playwright should cover default Active, inactive-only content, switching back, empty selected tab, create/deactivate/reactivate transitions, signed/partially signed agreement view/download preservation, company records beyond the first page, branch scopes, template tabs independent of submissions/monthly reports, admin/manager permissions and manager inactive shifts on desktop/mobile. Update existing checkbox/mixed-list tests to the new tab contract. Type-check/build and run relevant browser suites, then full CI. Review, merge to main and verify the published website asset; backend executable behavior is unchanged.
