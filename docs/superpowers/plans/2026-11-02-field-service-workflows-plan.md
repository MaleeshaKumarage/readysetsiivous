# Field-service workflows — reusable implementation plan

Approved design: [design](../specs/2026-11-02-field-service-workflows-design.md).
Baseline: main `b4f7b626` (2026-11-02). Progress/handoffs: [delivery ledger](../../implementation/field-service-progress.md).

## Delivery contract

One small PR at a time: implement → focused Playwright E2E/backend verification → review/fix → merge → save handoff → compact context → next chunk. Merge only after every CI check for the latest PR head has completed successfully and all actionable review comments are resolved; start the next chunk with fresh context after the saved handoff. A product stream is not a giant PR. Keep planned, implemented, merged and deployed status separate. Preserve unrelated files and normal verified deployment; no production data edits or release-gate shortcuts.

Each PR handoff records reviewed head/PR, interfaces, tests/results, review dispositions, deployment state, remaining decisions and next dependency-ready chunk. Never put credentials/customer data in the public handoff. Keep current light-English admin, FI/EN partner UI, roles/tenant boundaries and existing financial snapshots.

## Chunk order and dependencies

```text
C01 searchable company selectors
 ├─ A01 employee-owned autoaccept (confirmed exact admin start)
 └─ C02 paged customers → C03 customer workspace → C04 Individual customers
                                               → C05 guided prerequisites
W01 typed work queue → W02 shift workspace/handoffs → W03 completion exceptions
P01 effective rates → P02 earnings review → P03 approval/payment/statements
E01 private expenses → E02 approvals/settlement → E03 contextual expense links
B01 invoice tabs/context → B02 payment balances → B03 sending/corrections
D01 Needs attention dashboard (only after each contributing queue exists)
```

C03 supplies context to W/P/E/B. Reuse existing models/editors rather than parallel CRUD. General quotes are later W04; they do not block partner pricing or existing scheduling. First PR is **C01 only**, not customer migration or guided creation.

## C01 — searchable selectors (first PR)

- [ ] Replace first100 company loads in Agreements/Shifts with shared bounded server search using existing `GET /api/v1/admin/companies?search=&skip=&take=`.
- [ ] Retain selected record across query/page changes, initial context and edits; fetch existing selected ID by detail when absent from current results. Expose further results using returned total rather than silently truncating.
- [ ] Debounce queries; ignore stale/unmounted results. Separate loading/no matches/error with retry. Preserve lifecycle/role eligibility, create/update payloads and branch dependency clearing.
- [ ] E2E: find/select record beyond100 in both forms, rapid-search latest result, selected record retained, failed search/retry, keyboard and mobile selection, existing/inactive/foreign context boundaries.
- [ ] Run focused browser suite, typecheck/build as appropriate; review and fix before commit/merge. No Next build concurrent with Playwright sharing `.next`.

## A01 — employee-owned autoaccept (next small PR; timing confirmed)

- [ ] Default Manual (`AutoAcceptWork=false`) for all existing/new staff. Add self-owned `GET/PUT /api/v1/me/work-preferences` guarded by verified active work-role identity.
- [ ] Preference controls offered partner work only; it does not permit arbitrary self-assignment. Hide PartnerOffers approvals only after confirmed enabled state; accepted work remains visible and failures actionable.
- [ ] **Confirmed user decision:** admin must supply an exact planned start before an offer can be automatically accepted. Do not select an earliest/default time inside the window. Missing exact time fails clearly without creating a hidden pending offer.
- [ ] Use one lock-held acceptance/materialisation routine shared by Offer/Accept, reusing `IShiftWorkReservation`, single atomic commit and no nested Accept call. Preference update and Offer use the same work-reservation locking so enabling cannot race with creation of an invisible pending offer.
- [ ] Enabling is blocked clearly while any existing pending offers remain: instruct the employee to accept/decline them first, without silently hiding them. Disabling affects future offers only. Autoaccept never bypasses conflict/date/price checks. Send informational notifications, never approval-prompt emails. Implementation is authorised; no additional approval is needed for these confirmed boundaries.
- [ ] Backend/E2E: self-only/role/tenant guard, default/backfill, saved-state visibility, pending preference races, manual/automatic offer outcomes, conflict rollback/idempotent retry, date-window validation and accepted work visibility.

## C02–C05 — connected customers (separate PRs)

- **C02:** search, lifecycle server filter, deterministic paging and full-result counts (25 default;50/100 choices). Preserve old list callers. URL filter/page/size, Back restore and empty-final-page correction. Tests: filters/counts/paging/races/retry/Back/mobile.
- **C03:** keep `/admin/companies/` compatible; menu label Customers; customer-ID/section deep links and lazy Overview/Properties/Agreements/Work/Invoices/Expenses tabs. Expose only implemented/authorised tabs. Add shared context creation links and customer names to agreement summaries. Tests: lazy loads, direct link, Back, role isolation, contextual selection and lead conversion without duplicates.
- **C04:** Individual/Company type, historical default Company, reusable billing/contact details. Company business ID validation/uniqueness retained; individuals have no invented ID. Deny partner enablement/association to individuals on server. Tests: old-record migration/read, both creates/edits, duplicates, partner denial and existing references.
- **C05:** agreement Customer→Document/signers→Review; shift Customer/property→Schedule→Assignment/checklist→Review. Inline prerequisite create returns selected ID while retaining fields/PDF; cancel returns to task. Created prerequisites persist independently. Single mounted task, no nested modal stack/private browser draft storage. Dirty warning, dependent-value clearing and no signed-agreement scheduling gate. Tests: all forward/back/cancel/create/error/retry/double-submit/focus paths, including partial signer rows.

## W — operations (W01/W02/W03 separate PRs)

- **W01:** expose existing admin booking list/detail/actions and typed Work queue for public bookings, partner requests and unassigned work. Views Needs action/Scheduled/In progress/Completed; source/customer/property/date/assignee filters. Reuse original mutation APIs and preserve admin-only booking actions. Tests: every allowed/denied transition, conflict, source links and query scope.
- **W02:** compact shift rows, primary detail action and secondary menu; workspace sections for schedule/assignees/visits/checklist/pricing. Preserve This visit/Recurring/From date scopes, future overrides and completed history. Explain requested/awaiting acceptance/accepted/in progress/completed and next actor. Tests: all menus/scopes/partial conflicts, partner-company scope and own cleaner work across desktop/mobile.
- **W03:** missing-completion/unusual-time review; scheduled versus actual labels and existing reasoned correction actions. No automatic time edits. Checklist remains performed-activity measurement; unchecked items do not block finish. Preserve per-photo notes and deleted-source snapshots. Tests: existing past/today/future start policy, lost response, corrections/evidence and inactive visibility.
- **W04 later:** customer/property quote preparation, itemised net/VAT, validity, reviewed sending, acceptance/rejection and agreement/work links. Separate scoped spec must settle public-link auth/expiry/revocation and sent-version immutability. Existing partner pricing remains independent.

## P — employee payments (P01/P02/P03 separate PRs)

- **P01:** admin-only effective hourly rate history. Legacy PayRate needs explicit confirmation/effective date before historical use. Missing rate is an exception. Test rate boundaries and sensitive DTO/role isolation.
- **P02:** own completed visits × full planned duration × scheduled-date effective rate; group by scheduled local work month. Two completing workers get full duration each. Exclude cancelled/incomplete/already-paid work; no colleague-completion inference. Stable employee/visit settlement identity, late unpaid adjustments and reasoned amount/hours overrides. Test multiple workers, reassignment, corrections, missing rate/duration, DST, deleted source and stale review.
- **P03:** Draft→Approved→Paid; approval revalidation, immutable rate/duration/person/work snapshots and transactional duplicate prevention across runs/retries. Record external paid date/reference; linked corrections after approval/payment. Own employee statements/accountant exports. Test racing approval/pay, changed source/rate, lost response, late work, role isolation and export totals. Actual duration corrections do not silently change planned pay.

## E — expenses (E01/E02/E03 separate PRs)

- **E01:** employees/managers submit own private receipts; admins record company costs. Date/category/supplier/amount/currency/description/payer, optional customer/property/visit links. Distinguish company-paid versus employee-paid claims. Test file bounds/access, failed upload/retry and forged references.
- **E02:** admin Needs approval/Approved-unpaid/Paid/Rejected views, rejection reason/revision/resubmission and audited external settlement. No duplicate reimbursement or paid-record rewrite. No tax-deductibility engine. Test lifecycle/concurrency/receipt permission and company costs never creating reimbursement debt.
- **E03:** contextual links show the same expense, never copies. Employee summary separates earnings/reimbursement subtotals. Test no double totals or unauthorised receipt/financial data leakage.

## B — billing (B01/B02/B03 separate PRs)

- **B01:** History default/Create/Settings; context preselects customer/period/eligible work, explains exclusions, reuses billing details with review. Preserve numbering/preview/idempotency/immutable issue/languages/PDF/barcode and customer pricing independent from wages. Test manual/shift invoices, monthly rules, actual completion-month partner billing, address edits and uncertain issue retry.
- **B02:** append-only amount/date/reference/actor payment entries; derive outstanding/partial/paid/overdue. Preserve old mark-paid through compatible remaining-balance settlement. Test multiple/invalid/duplicate payments, stale balance, due dates, void conflict and immutable invoice history.
- **B03:** explicit reviewed email/reminder with recipient/document preview and delivery outcome, no auto campaigns. Linked adjustment/credit documents rather than issued snapshot edits; settle numbering/source-lock reconciliation in the chunk spec. Test failed send/retry, correction balance/history and original PDF unchanged.

## D01 and programme acceptance

- Permission-scoped real server counts for unassigned/pending acceptance/missing completion/unpaid invoices/expense approvals/pay exceptions; links restore corresponding filtered queue. Central issuer Settings with invoice shortcut. Test role/tenant counts, transitions, zero/error/retry states and no dead/unimplemented navigation.
- Shared fixture:100customers500properties100staff plus multiyear work/finance. Record beyond100 selectable; bounded lists/counts correct. Browser keyboard/focus/labels, phone/tablet/desktop containment and installed-PWA light-admin coverage.
- Cross-stream server/browser coverage: foreign IDs/roles, stale versions, duplicate/lost-response/concurrent writes, retained approved/issued/paid snapshots after operational rename/deactivation/deletion. Update feature docs only for delivered capabilities.
