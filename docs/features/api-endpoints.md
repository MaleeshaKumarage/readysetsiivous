# HTTP endpoint inventory

Source baseline: `ceff5bd`, reviewed 2026-10-13. Generated from every controller's
route and HTTP attributes, then cross-checked against the feature catalogue.
`staff` policy means **admin OR employee**, not the literal Keycloak role `staff`.
Additional method-level admin policies narrow inherited staff access. Tenant
resolution/active-status checks also apply; the listed role is not the entire
security boundary. System controller checks the configured provisioner realm.

`{slug}` is the public tenant slug. `{id:guid}` indicates a GUID route constraint;
a malformed segment can fail routing with 404 before validation. Request bodies
and query contracts are defined by linked controller signatures and application
commands; do not infer them from the frontend helper alone. All IDs are scoped
to the request tenant unless the handler explicitly acts on the registry. QC
persistence now uses the tenant session; legacy partition migration is required
before deploying over pre-existing default-partition QC data.

| Method | Route | Authorization | Action / evidence |
| --- | --- | --- | --- |
| POST | `/api/v1/admin/agreements` | admin | [Create](../../api/CleaningSuite.Api/Controllers/AgreementsAdminController.cs#L19) |
| GET | `/api/v1/admin/agreements` | admin OR employee | [List](../../api/CleaningSuite.Api/Controllers/AgreementsAdminController.cs#L31) |
| GET | `/api/v1/admin/agreements/{id:guid}` | admin OR employee | [Get](../../api/CleaningSuite.Api/Controllers/AgreementsAdminController.cs#L36) |
| GET | `/api/v1/admin/agreements/{id:guid}/document` | admin OR employee | [Document](../../api/CleaningSuite.Api/Controllers/AgreementsAdminController.cs#L43) |
| POST | `/api/v1/admin/agreements/{id:guid}/signers` | admin | [AddSigner](../../api/CleaningSuite.Api/Controllers/AgreementsAdminController.cs#L51) |
| DELETE | `/api/v1/admin/agreements/{id:guid}/signers/{signerId:guid}` | admin | [RemoveSigner](../../api/CleaningSuite.Api/Controllers/AgreementsAdminController.cs#L59) |
| POST | `/api/v1/admin/agreements/{id:guid}/cancel` | admin | [Cancel](../../api/CleaningSuite.Api/Controllers/AgreementsAdminController.cs#L67) |
| POST | `/api/v1/admin/agreements/{id:guid}/activate` | admin | [Activate](../../api/CleaningSuite.Api/Controllers/AgreementsAdminController.cs#L75) |
| POST | `/api/v1/admin/agreements/{id:guid}/deactivate` | admin | [Deactivate](../../api/CleaningSuite.Api/Controllers/AgreementsAdminController.cs#L83) |
| GET | `/api/v1/public/{slug}/agreements/{token}` | anonymous | [Get](../../api/CleaningSuite.Api/Controllers/AgreementsPublicController.cs#L17) |
| POST | `/api/v1/public/{slug}/agreements/{token}/sign` | anonymous | [Sign](../../api/CleaningSuite.Api/Controllers/AgreementsPublicController.cs#L24) |
| GET | `/api/v1/public/{slug}/agreements/{token}/file` | anonymous | [File](../../api/CleaningSuite.Api/Controllers/AgreementsPublicController.cs#L32) |
| GET | `/api/v1/admin/bookings` | admin OR employee | [List](../../api/CleaningSuite.Api/Controllers/BookingsAdminController.cs#L20) |
| GET | `/api/v1/admin/bookings/{id:guid}` | admin OR employee | [Detail](../../api/CleaningSuite.Api/Controllers/BookingsAdminController.cs#L32) |
| POST | `/api/v1/admin/bookings/{id:guid}/confirm` | admin | [Confirm](../../api/CleaningSuite.Api/Controllers/BookingsAdminController.cs#L39) |
| POST | `/api/v1/admin/bookings/{id:guid}/cancel` | admin | [Cancel](../../api/CleaningSuite.Api/Controllers/BookingsAdminController.cs#L47) |
| POST | `/api/v1/admin/bookings/{id:guid}/complete` | admin | [Complete](../../api/CleaningSuite.Api/Controllers/BookingsAdminController.cs#L55) |
| POST | `/api/v1/admin/bookings/{id:guid}/assign` | admin | [Assign](../../api/CleaningSuite.Api/Controllers/BookingsAdminController.cs#L63) |
| POST | `/api/v1/admin/bookings/{id:guid}/unassign` | admin | [Unassign](../../api/CleaningSuite.Api/Controllers/BookingsAdminController.cs#L75) |
| GET | `/api/v1/admin/companies` | admin | [List](../../api/CleaningSuite.Api/Controllers/CompaniesAdminController.cs#L24) |
| GET | `/api/v1/admin/companies/{id:guid}` | admin | [Get](../../api/CleaningSuite.Api/Controllers/CompaniesAdminController.cs#L38) |
| POST | `/api/v1/admin/companies` | admin | [Create](../../api/CleaningSuite.Api/Controllers/CompaniesAdminController.cs#L45) |
| PUT | `/api/v1/admin/companies/{id:guid}` | admin | [Update](../../api/CleaningSuite.Api/Controllers/CompaniesAdminController.cs#L52) |
| POST | `/api/v1/admin/companies/{id:guid}/deactivate` | admin | [Deactivate](../../api/CleaningSuite.Api/Controllers/CompaniesAdminController.cs#L59) |
| POST | `/api/v1/admin/companies/{companyId:guid}/branches` | admin | [CreateBranch](../../api/CleaningSuite.Api/Controllers/CompaniesAdminController.cs#L66) |
| PUT | `/api/v1/admin/companies/branches/{branchId:guid}` | admin | [UpdateBranch](../../api/CleaningSuite.Api/Controllers/CompaniesAdminController.cs#L73) |
| POST | `/api/v1/admin/companies/branches/{branchId:guid}/deactivate` | admin | [DeactivateBranch](../../api/CleaningSuite.Api/Controllers/CompaniesAdminController.cs#L80) |
| GET | `/api/v1/admin/employees` | admin OR employee | [List](../../api/CleaningSuite.Api/Controllers/EmployeesAdminController.cs#L21) |
| POST | `/api/v1/admin/employees` | admin | [Create](../../api/CleaningSuite.Api/Controllers/EmployeesAdminController.cs#L29) |
| PUT | `/api/v1/admin/employees/{id:guid}` | admin | [Update](../../api/CleaningSuite.Api/Controllers/EmployeesAdminController.cs#L37) |
| POST | `/api/v1/admin/employees/{id:guid}/deactivate` | admin | [Deactivate](../../api/CleaningSuite.Api/Controllers/EmployeesAdminController.cs#L45) |
| POST | `/api/v1/admin/employees/{id:guid}/invite` | admin | [Invite](../../api/CleaningSuite.Api/Controllers/EmployeesAdminController.cs#L54) |
| GET | `/api/v1/admin/invoices` | admin OR employee | [List](../../api/CleaningSuite.Api/Controllers/InvoicesAdminController.cs#L21) |
| POST | `/api/v1/admin/invoices` | admin | [Create](../../api/CleaningSuite.Api/Controllers/InvoicesAdminController.cs#L29) |
| GET | `/api/v1/admin/invoices/{id:guid}` | admin OR employee | [Detail](../../api/CleaningSuite.Api/Controllers/InvoicesAdminController.cs#L37) |
| POST | `/api/v1/admin/invoices/{id:guid}/mark-paid` | admin | [MarkPaid](../../api/CleaningSuite.Api/Controllers/InvoicesAdminController.cs#L44) |
| POST | `/api/v1/admin/invoices/{id:guid}/void` | admin | [Void](../../api/CleaningSuite.Api/Controllers/InvoicesAdminController.cs#L53) |
| GET | `/api/v1/admin/invoices/{id:guid}/pdf` | admin OR employee | [Pdf](../../api/CleaningSuite.Api/Controllers/InvoicesAdminController.cs#L62) |
| GET | `/api/v1/me` | authenticated | [Get](../../api/CleaningSuite.Api/Controllers/MeController.cs#L13) |
| GET | `/api/v1/public/{slug}/availability` | anonymous | [Availability](../../api/CleaningSuite.Api/Controllers/PublicBookingsController.cs#L19) |
| POST | `/api/v1/public/{slug}/bookings` | anonymous | [Book](../../api/CleaningSuite.Api/Controllers/PublicBookingsController.cs#L27) |
| GET | `/api/v1/public/{slug}/bookings/{bookingNumber}` | anonymous | [Lookup](../../api/CleaningSuite.Api/Controllers/PublicBookingsController.cs#L34) |
| POST | `/api/v1/public/{slug}/bookings/{bookingNumber}/cancel` | anonymous | [Cancel](../../api/CleaningSuite.Api/Controllers/PublicBookingsController.cs#L42) |
| GET | `/api/v1/public/{slug}/services` | anonymous | [Services](../../api/CleaningSuite.Api/Controllers/PublicController.cs#L19) |
| GET | `/api/v1/public/{slug}/content` | anonymous | [Content](../../api/CleaningSuite.Api/Controllers/PublicController.cs#L26) |
| GET | `/api/v1/admin/quality-cycle/templates` | admin | [ListTemplates](../../api/CleaningSuite.Api/Controllers/QualityCycleAdminController.cs#L21) |
| GET | `/api/v1/admin/quality-cycle/templates/{id:guid}` | admin | [GetTemplate](../../api/CleaningSuite.Api/Controllers/QualityCycleAdminController.cs#L31) |
| POST | `/api/v1/admin/quality-cycle/templates` | admin | [CreateTemplate](../../api/CleaningSuite.Api/Controllers/QualityCycleAdminController.cs#L38) |
| PUT | `/api/v1/admin/quality-cycle/templates/{id:guid}` | admin | [UpdateTemplate](../../api/CleaningSuite.Api/Controllers/QualityCycleAdminController.cs#L45) |
| DELETE | `/api/v1/admin/quality-cycle/templates/{id:guid}` | admin | [DeleteTemplate](../../api/CleaningSuite.Api/Controllers/QualityCycleAdminController.cs#L53) |
| POST | `/api/v1/admin/quality-cycle/dispatch` | admin | [DispatchForms](../../api/CleaningSuite.Api/Controllers/QualityCycleAdminController.cs#L60) |
| GET | `/api/v1/admin/quality-cycle/forms` | admin | [ListForms](../../api/CleaningSuite.Api/Controllers/QualityCycleAdminController.cs#L67) |
| GET | `/api/v1/admin/quality-cycle/summary-pdf` | admin | [DownloadSummaryPdf](../../api/CleaningSuite.Api/Controllers/QualityCycleAdminController.cs#L78) |
| GET | `/api/v1/public/{slug}/quality-cycle/{token}` | anonymous | [GetByToken](../../api/CleaningSuite.Api/Controllers/QualityCyclePublicController.cs#L20) |
| POST | `/api/v1/public/{slug}/quality-cycle/{token}/submit` | anonymous | [Submit](../../api/CleaningSuite.Api/Controllers/QualityCyclePublicController.cs#L27) |
| GET | `/api/v1/admin/schedule` | admin | [Get](../../api/CleaningSuite.Api/Controllers/ScheduleController.cs#L19) |
| GET | `/api/v1/admin/services` | admin OR employee | [List](../../api/CleaningSuite.Api/Controllers/ServicesAdminController.cs#L26) |
| POST | `/api/v1/admin/services` | admin | [Create](../../api/CleaningSuite.Api/Controllers/ServicesAdminController.cs#L34) |
| PUT | `/api/v1/admin/services/{id:guid}` | admin | [Update](../../api/CleaningSuite.Api/Controllers/ServicesAdminController.cs#L42) |
| DELETE | `/api/v1/admin/services/{id:guid}` | admin | [Delete](../../api/CleaningSuite.Api/Controllers/ServicesAdminController.cs#L50) |
| POST | `/api/v1/admin/services/{id:guid}/image` | admin | [UploadImage](../../api/CleaningSuite.Api/Controllers/ServicesAdminController.cs#L59) |
| GET | `/api/v1/admin/shifts` | admin | [List](../../api/CleaningSuite.Api/Controllers/ShiftsAdminController.cs#L22) |
| GET | `/api/v1/admin/shifts/{id:guid}` | admin | [Get](../../api/CleaningSuite.Api/Controllers/ShiftsAdminController.cs#L33) |
| POST | `/api/v1/admin/shifts` | admin | [Create](../../api/CleaningSuite.Api/Controllers/ShiftsAdminController.cs#L40) |
| PUT | `/api/v1/admin/shifts/{id:guid}` | admin | [Update](../../api/CleaningSuite.Api/Controllers/ShiftsAdminController.cs#L47) |
| POST | `/api/v1/admin/shifts/{id:guid}/deactivate` | admin | [Deactivate](../../api/CleaningSuite.Api/Controllers/ShiftsAdminController.cs#L55) |
| GET | `/api/v1/admin/shifts/{id:guid}/assignments` | admin | [GetAssignments](../../api/CleaningSuite.Api/Controllers/ShiftsAdminController.cs#L62) |
| GET | `/api/v1/admin/shifts/{id:guid}/occurrences` | admin | [Occurrences](../../api/CleaningSuite.Api/Controllers/ShiftsAdminController.cs#L69) |
| POST | `/api/v1/admin/shifts/{id:guid}/assignments` | admin | [AssignEmployee](../../api/CleaningSuite.Api/Controllers/ShiftsAdminController.cs#L80) |
| DELETE | `/api/v1/admin/shifts/{id:guid}/assignments/{employeeId:guid}` | admin | [RemoveEmployee](../../api/CleaningSuite.Api/Controllers/ShiftsAdminController.cs#L93) |
| POST | `/api/v1/system/tenants` | authenticated + configured provisioner realm | [Provision](../../api/CleaningSuite.Api/Controllers/SystemController.cs#L23) |
| GET | `/api/v1/system/tenants` | authenticated + configured provisioner realm | [List](../../api/CleaningSuite.Api/Controllers/SystemController.cs#L35) |
| POST | `/api/v1/system/tenants/{slug}/suspend` | authenticated + configured provisioner realm | [Suspend](../../api/CleaningSuite.Api/Controllers/SystemController.cs#L45) |
| POST | `/api/v1/system/tenants/{slug}/activate` | authenticated + configured provisioner realm | [Activate](../../api/CleaningSuite.Api/Controllers/SystemController.cs#L55) |
| GET | `/api/v1/admin/tenant` | admin OR employee | [Get](../../api/CleaningSuite.Api/Controllers/TenantAdminController.cs#L20) |
| PUT | `/api/v1/admin/tenant` | admin | [Update](../../api/CleaningSuite.Api/Controllers/TenantAdminController.cs#L27) |
| PUT | `/api/v1/admin/tenant/pages/{pageKey}` | admin | [UpdatePage](../../api/CleaningSuite.Api/Controllers/TenantAdminController.cs#L35) |

Total: **77 controller endpoints**. Non-controller public entries:

- `GET /healthz`: liveness only, no database/auth dependency.
- `/uploads/<GUID>.<jpg/jpeg/png/webp>`: static public root service images only;
  agreement subdirectories are excluded by PublicUploadsFileProvider.
- `/swagger`/OpenAPI: Development-only middleware.
- Frontend route/metadata/static assets are documented in the feature inventory.

## Contract traps to preserve in tests

- Public booking creation returns 201 with bookingId/bookingNumber/customerReference.
- Admin service/employee create returns 201 with `id`; company/branch/shift/QC and
  agreement create response shapes differ. Read the controller/command DTO.
- Public booking lookup and cancellation both use the `phone` query parameter.
- Admin agreement create requires multipart fields `title`, `companyId`, `pdf`,
  `signersJson`; service image upload is multipart `file`. Do not set a JSON
  Content-Type header for FormData.
- Shift assignment route ID overrides body ShiftId. Removal identifies employee,
  not assignment ID, in the route. Shift schedules use numeric enums and HH:mm
  TimeSpan inputs; outputs can use HH:mm:ss.
- Public QC submit route token overrides body's Token. The body carries items,
  photoUrls and cleanerNotes. Photo URLs currently include FileReader data URLs.
- Agreement sign accepts typedName and signaturePng (base64/data URI), not a raw
  canvas upload. File retrieval is a separate token-authenticated public request.
- QC summary query uses shiftId/year/month; filters and PDF routes may carry query
  strings, which route mocks must match.
- RFC 7807 errors commonly use status/title/detail/errors. Public rate-limit429
  currently has no JSON body. Some unmapped domain exceptions return500.
- Native browser download/opening an admin PDF URL does not add Authorization.
  The FE currently fetches blobs with a bearer token for supported downloads.

## QC workflow additions (2026-10-13)

The baseline inventory above has 77 routes; these seven additions bring it to 84.
All admin routes require the current tenant's `admin` realm role.

| Method | Route | Role | Contract |
| --- | --- | --- | --- |
| PUT | `/api/v1/admin/shifts/{id:guid}/quality-cycle-template` | tenant admin | `{templateId: GUID or null}`; attaches/changes/removes, ensures upcoming records |
| GET | `/api/v1/admin/quality-cycle/monthly-summary` | tenant admin | year/month; optional companyId/branchId/shiftId; per-shift activity day/occurrence totals |
| GET | `/api/v1/admin/quality-cycle/branch-summary-pdf` | tenant admin | branchId/year/month; separate shift sections |
| POST | `/api/v1/admin/quality-cycle/report-shares` | tenant admin | year/month plus companyId/branchId/shiftId scope; frozen summary token with 30-day expiry |
| GET | `/api/v1/admin/quality-cycle/report-shares` | tenant admin | own-tenant shared link metadata |
| DELETE | `/api/v1/admin/quality-cycle/report-shares/{id:guid}` | tenant admin | revoke own-tenant link |
| GET | `/api/v1/public/{slug}/quality-cycle/reports/{token}/pdf` | anonymous token holder | summary-only PDF; expired/revoked/unknown404; no-store/noindex |

Existing `summary-pdf` now uses the activity report builder. Existing submission
requires exact checklist structure, permits any checked subset and empty notes,
and returns Recorded task status. Repeat/pre-start attempts are409; invalid
checklist structure400. Month filtering is exclusive at the next month's start.
