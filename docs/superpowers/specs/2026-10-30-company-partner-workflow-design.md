# Company-scoped partner cleaning workflow

## Approval and user-visible outcome

The owner approved the proposed plan and requested implementation on 30 October 2026. A partner representing one enabled company can manage cleaning requests for its properties; tenant staff price properties and offer jobs to cleaners; accepted work runs through existing My Shifts and manual monthly invoicing.

## Locked product decisions

- New `partner` role, exactly one current company binding. Company partner access defaults off. Tenant admins enable companies and create/invite partner accounts through the existing employee UI.
- Partners see only their own company's branches and shifts. They are customers, not assignable staff. Tenant/platform management, payroll, other customers and staff personal data are excluded.
- Branch room count and bed count are optional descriptive fields. Price is a reusable fixed net amount per cleaning, with VAT and staff-estimated duration; it is not derived from counts or actual hours.
- Partners create branches and request their first quote. Admins/managers can create and price branches directly. Staff-set prices become usable immediately, with no partner quote-acceptance stage.
- Priced property changes go through staff. Existing unpriced branches remain visible but cannot be used to request cleaning until price and planning duration are ready.
- Each cleaning request means one visit: a flexible single date/inclusive date range, or one fixed date and exact start time. Staff may preselect a time; otherwise the offered cleaner chooses one while accepting. The whole planned visit fits the requested window.
- Partners may edit dates/instructions or cancel only before the first acceptance. Editing invalidates outstanding offers but retains the original pricing snapshot. First acceptance permanently closes direct partner editing, including during later reassignment. Staff-change requests handle later changes.
- Acceptance creates at most one ordinary one-time shift. Decline returns the request to staff. Staff reassignment reuses the linked shift and requires renewed acceptance. Started/billed work must not be silently rewritten.
- Cleaner actual start/end remains the existing My Shifts workflow, independent of estimated duration and fixed price. Optional checklists remain optional.
- Portal and email notifications cover committed quote, request, offer and lifecycle changes. Email delivery failure does not undo successful work.
- Tenant admin manually previews/issues a company/month invoice for completed, unbilled visits using the frozen request net price/VAT. No automatic monthly generation/issue/email. Scheduled local visit date determines the invoice period.
- Authenticated partner/staff UI remains English and uses the current light workspace components. Public website redesign and its three-language switch are outside this feature.

## Existing implementation facts

- `Employee` is already the application-owned invited-user profile. Add a nullable partner company binding without granting staff privileges.
- Keycloak uses fixed groups and a guarded server extension. Extend exact allowlists and explicit setup, not runtime Admin REST privileges.
- All Marten documents are conjoined-tenant partitioned and optimistic-concurrency checked. `IShiftWorkReservation` provides a cross-replica scheduling lock and fresh serializable session.
- Existing repositories call `SaveChangesAsync` themselves. Acceptance needs a dedicated unit of work, not a sequence of those save methods.
- Existing one-time work is `OnCallFlexible` plus an `Extra` occurrence. No recurring schedule or new schedule enum is needed.
- Existing work forms record actual start/end and submitted completion even with no checklist template. Existing per-occurrence invoice calculation can consume these facts.
- Invoice preview is nonpersisting; issue locks, rebuilds and checks the preview fingerprint. Issued/paid visits are billed; voided invoices release eligibility. Partner month selection must not silently inherit the manual selector's 50-shift limit.
- Resend reminder delivery provides a useful durable/idempotent transport pattern; partner business changes need their own transactional notification intents.

## Access and compatibility invariants

Partner tenant/company/active-role scope comes from current database identity on every endpoint, never a client company ID or stale JWT. Reject mixed partner/staff roles, missing bindings and disabled companies. Keep staff/operations policies unchanged. Use separate safe DTOs for partner, cleaner and staff contexts; cleaner offers never disclose customer pricing/payroll, and partners receive a cleaner display name only after acceptance.

Validate binding and branch readiness again under the scheduling reservation. Company/branch deactivation blocks new partner writes without erasing history. Protect lifecycle operations through legacy shift/branch/assignment routes as well as new endpoints. Invitation redemption and reinvitation cannot bypass disabled-company access checks.

Freeze request price, VAT, duration and pricing revision server-side. Use a request revision plus database concurrency, idempotent client request identity and a unique/deterministic request-to-shift relationship. Acceptance, assignment, billing snapshot and outbox intents commit once. Before-first-acceptance edits and cancellations revoke stale offers. Repeated successful acceptance returns the established result rather than creating work again.

## Verification and release

Test authorization with two companies/two tenants, stale identities, forged IDs, role mixtures and all legacy mutation entry points. Test flexible/fixed windows, timezone/DST validation, overlap checks, decline/reoffer, simultaneous acceptance/edit/cancel, price changes and no-checklist actual work. Test complete company/month invoicing beyond 50 visits and issue concurrency. Add browser coverage across invitation, quote, request, acceptance, actual completion, owner status and invoice selection.

Use development-only native Keycloak verification before production promotion. New fields are additive and existing company flags remain false; do not invent/backfill prices or enable customers automatically. Preserve existing guarded releases and require rollback compatibility with the actual deployed baseline.
