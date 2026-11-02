# Partner API contract

All endpoints use tenant JWT authentication and uncached responses. Company scope
for partner routes comes from the current database profile, never a request field.
All timestamps ending `Utc` are UTC ISO strings; dates are `YYYY-MM-DD`, exact
times are `HH:mm`, and plannedStartLocal is an offset-free `YYYY-MM-DDTHH:mm` in
the returned tenant time zone. Versions are integer request revisions.

## Shared DTOs

- Company appends `isPartnerEnabled: boolean`.
- Branch appends nullable `roomCount`, `bedCount`, `priceNet`, `vatRatePercent`,
  `estimatedDurationMinutes`, plus `pricingVersion: number`, `pricingReady: boolean`,
  `quoteRequestedUtc: string|null`, `sizeSquareMeters: number|null`, and `preferredCleanerEmployeeId: string|null`. Existing branch address/contact fields remain.
- Partner identity: `{realm, person:{id,firstName,lastName,role,isActive}, company:{id,name,isActive,isPartnerEnabled}, timeZoneId}`.
- Cleaning request: `{id,version,companyId,branchId,branchName,fromDate,toDate,exactStartLocalTime,notes,priceNet,vatRatePercent,estimatedDurationMinutes,status,firstAcceptedAtUtc,linkedShiftId,plannedStartUtc,plannedEndUtc,acceptedCleanerName,actualStartUtc,actualEndUtc}`.
- Change request: `{id,companyId,branchId,requestId,kind,message,createdUtc,resolvedUtc,resolution}`.
- Cleaner offer: `{id,requestId,version,branchName,street,postalCode,city,fromDate,toDate,exactStartLocalTime,plannedStartUtc,estimatedDurationMinutes,notes,timeZoneId}`. No customer price or staff-private data.
- Safe company shift: `{id,branchId,branchName,name,isActive,schedule,validFrom,validUntil}`. Internal shift notes are omitted.

### Updates from existing clients

Company updates that omit `isPartnerEnabled` (or send null) retain the current flag;
an explicit `false` disables partner access. Branch updates preserve omitted room/bed
counts, while an explicit null clears the selected optional count. Omitting all pricing
fields, or sending all three as null, retains current pricing; changing pricing requires
net price, VAT and estimated duration together. Existing request price snapshots are unchanged.

Partner company membership is application database state, not a Keycloak/JWT permission.
Rebinding an active partner to another enabled company therefore keeps the same identity
role and checks the new current company on every request; it does not require an identity
role synchronization or grant access to the previous company through a cached token claim.

## Partner `/api/v1/partner`

- `GET /me`, `GET /branches` => identity, Branch[].
- `POST /branches` body `{name?,street?,postalCode?,city?,country?,contactPhone?,contactEmail?,roomCount?,bedCount?,sizeSquareMeters?}` => Branch. The property name is generated from the address (`Street, PostalCode City`); provide at least a street, postal code or city. The server ignores a submitted name for partner properties.
- `POST /branches/{id}/quote-request` => Branch (idempotent until priced).
- `POST /branches/{id}/change-requests` body `{message}` => change request.
- `GET /requests` => cleaning request[]. `POST /requests` body `{clientRequestId,branchId,fromDate,toDate,exactStartLocalTime?,notes?}` => cleaning request.
- `PUT /requests/{id}` body `{version,fromDate,toDate,exactStartLocalTime?,notes?}` => cleaning request.
- `POST /requests/{id}/cancel` body `{version}` => cleaning request.
- `POST /requests/{id}/change-requests` body `{message}` => change request.
- `GET /shifts` => safe company shift[].
- `GET /change-requests` => own-company change request[] including staff resolutions.

## Staff `/api/v1/admin/partner` and `/api/v1/manager/partner`

- `GET /requests?companyId=` => cleaning request[] with extra `offeredEmployeeId` (nullable), `offerId` (nullable).
- `GET /quotes` => Branch[] with companyId; unpriced quote-requested branches.
- `GET /change-requests` => change request[].
- `POST /change-requests/{id}/resolve` body `{resolution}` => change request.
- `GET /branches?companyId=` => Branch[].
- `POST /branches` body is existing CreateBranchCommand plus optional pricing/count fields => Branch.
- `PUT /branches/{id}` body is existing UpdateBranchCommand plus optional pricing/count fields => Branch.
- `PUT /branches/{id}/pricing` body `{priceNet,vatRatePercent,estimatedDurationMinutes}` => Branch.
- `POST /requests/{id}/offer` body `{version,employeeId,plannedStartLocal?}` => cleaning request.
- `PUT /requests/{id}` body `{version,fromDate,toDate,exactStartLocalTime?,notes?}` => cleaning request. Staff rescheduling preserves the linked shift and financial snapshot, withdraws offers and requires fresh cleaner acceptance. Started or billed visits cannot be changed.
- `POST /requests/{id}/cancel` body `{version,reason}` => cleaning request.

## Cleaner `/api/v1/me/partner-offers`

- `GET /` => cleaner offer[]. Only offers addressed to the authenticated staff profile.
- `POST /{offerId}/accept` body `{version,plannedStartLocal?}` => `{id,version,status,linkedShiftId,plannedStartUtc,plannedEndUtc}`. Customer prices are never returned to cleaners.
- `POST /{offerId}/decline` body `{version,reason?}` => 204.

States: Requested, Offered, Accepted, InProgress, Completed, Cancelled. Declining
returns a request to Requested. First acceptance permanently ends direct partner
editing/cancellation. Staff changes use the same linked shift and renew acceptance.
Errors follow existing problem details: validation 400, inaccessible ID 404,
inactive/unauthorized identity 403, stale version/lifecycle/scheduling conflict 409.

## Partner monthly invoices

Existing admin shift invoice preview/issue routes accept `partnerMonth: true`.
Use `companyId`, `shiftIds: []`, and inclusive `from`/`to` spanning one complete calendar month;
monthly overrides are not accepted in this mode. The server enumerates all linked completed,
unbilled partner visits, including more than 50 visits, using each request's frozen net price
and VAT. Existing manual 1–50 shift selection remains unchanged. Preview never claims visits;
issue re-evaluates under the existing tenant reservation and requires its unchanged preview token.
Issued and paid invoices claim a visit; voided invoices release it. The period uses the scheduled
tenant-local visit date, independent of actual start/end timestamps.

## Partner notification delivery

Business transitions atomically write `PartnerNotification` intents. The background dispatcher
checks current tenant/company/recipient eligibility, persists immutable delivery payloads before
sending and uses stable Resend idempotency keys. Provider delivery uses a nonblocking
per-intent cross-replica reservation rather than holding the tenant scheduling lock across
network calls. Transient failures retry with bounded backoff;
uncertain deliveries stop after 20 hours or 12 attempts for review. Changed recipients are skipped
rather than retargeting a previously attempted idempotency key. Actual work start/completion is
reconciled into deterministic intents from existing work forms; this never changes actual times.
All email links require sign-in; access instructions and cleaner notes are excluded.

## Branch routing and cleaner approval (2026-11-02)

Admin/manager branch create/update and branch responses include `sizeSquareMeters` (positive decimal, maximum 1000000, two decimal places) and optional `preferredCleanerEmployeeId`. Updates preserve omitted fields; explicit JSON null clears them. Preferred cleaners must be active, synchronized work-role employees with a linked login in the current tenant. Partner-created properties accept size but cannot choose cleaner, company or price.

For partner-enabled companies, new/edited branch names are generated from `Street, PostalCode City`; ordinary company branches retain manual names. Existing branches are not bulk-renamed. Branch/property lists sort by postal code then name.

`GET /api/v1/me/partner-request-preferences` and `PUT` at the same route read/update `{ "autoAcceptPartnerRequests": false }` for the verified current staff member. Existing employees require approval by default; administrative employee updates cannot set this preference.

New partner requests for a valid configured cleaner become ordinary `Offered` requests, with the existing cleaner offer/accept/decline endpoints and revision checks. The cleaner receives email and an optional generic push; administrators receive no initial broadcast. Explicit cleaner opt-in permits automatic acceptance only for an exact future appointment using the existing scheduling checks. Date ranges/flexible requests remain actionable offers even when automatic acceptance is enabled. Scheduling conflict leaves an offer for approval; it never fabricates a time or accepted visit. Missing/inactive/unlinked cleaners return the request to staff routing; decline returns to staff for reassignment. Existing accepted/history requests and frozen financial terms remain unchanged.

The branch-specific automatic approval preference applies only to future exact-time requests. Existing pending offers remain visible and manually actionable, including flexible/date-range offers. This does not implement the planned global approval-mode behavior (A01). Manager company responses include `isPartnerEnabled` so partner-property title generation matches the administrative flow.
