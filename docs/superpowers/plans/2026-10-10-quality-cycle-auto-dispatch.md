# Quality Cycle — Shift Clock-in/out + Inline Form (Reworked Plan)

> **Status:** Supersedes the email/background-service design. Reworked 2026-10-10.

**Goal:** Employee logs in, sees own shifts, presses **Start** (records actual start time), later fills the Quality Cycle form inline and presses **End** (records actual end + submits form). Everything recorded. Monthly PDF report per employee/branch/shift unchanged.

**Why reworked:** email + 5-min background auto-dispatch replaced by explicit Start/End buttons in an employee portal. Better UX, no background infra, no email.

**Flow:**
1. Employee opens app → **My Shifts** → sees assigned shifts + their occurrences + clock/form state.
2. **Start** → creates `QualityCycleForm` record with `StartedAtUtc` (checklist empty).
3. Later → **End** → fills checklist inline → submit → `EndedAtUtc` + `IsSubmitted=true`.

**Tech Stack (unchanged):** ASP.NET Core 8, Marten, MediatR, FluentValidation, xUnit + Moq, QuestPDF (`IQualityCyclePdfGenerator`), Next.js + Mantine, Keycloak (realm roles `admin`/`employee`).

**Spec:** `docs/superpowers/specs/2026-10-10-quality-cycle-auto-dispatch-design.md` (report section unchanged).

## Global Constraints

- .NET 8, nullable enabled; Marten docs inherit `BaseDocument`; optimistic concurrency.
- Multi-tenant: request-scoped `ITenantContext.TenantId`; employee endpoints resolve employee via Keycloak `sub` → `Employee.KeycloakUserId`.
- RFC 7807 errors; 409 conflicts; 404 missing.
- UTC in storage, Europe/Helsinki at edges. Money decimal.
- **No email, no background service, no backfill** — clock/flow is button-driven.

---

### Task 1: Domain — clock fields on `QualityCycleForm`

- Modify `api/CleaningSuite.Domain/QualityCycle/QualityCycleForm.cs`:
  - Add `DateTime? StartedAtUtc`, `DateTime? EndedAtUtc`.
  - Add `Start()` → sets `StartedAtUtc = DateTime.UtcNow` (no-op if already started).
  - Extend `Submit(...)` → set `EndedAtUtc = DateTime.UtcNow` when submitting.
- Test: `api/CleaningSuite.Tests/QualityCycle/QualityCycleFormTests.cs` — Start sets StartedAtUtc; Submit sets EndedAtUtc + IsSubmitted.

### Task 2: Application — Start/End commands + MyShifts query

- `StartQualityCycleFormCommand(Guid ShiftId, DateTime OccurrenceStartUtc) : IRequest<QualityCycleFormDto>` — resolves current employee (from `IEmployeeRepository.GetByKeycloakUserIdAsync`), creates form (idempotent via `GetFormByShiftOccurrenceAsync`), calls `Start()`, saves.
- `EndQualityCycleFormCommand(Guid FormId, List<QualityCycleFormItemDto> Items, List<string>? PhotoUrls, string? CleanerNotes) : IRequest<QualityCycleFormDto>` — loads form, verifies employee owns it, `Submit(...)`.
- `GetMyShiftsQuery(Guid EmployeeId, DateTime FromUtc, DateTime ToUtc) : IRequest<IReadOnlyList<MyShiftDto>>` — list employee's assignments → shifts → occurrences (`ShiftScheduleCalculator.GenerateOccurrences`) + each occurrence's form/clock state.
- Tests for all three.

### Task 3: API — employee endpoints

- Extend `MeController` (or new `MyShiftsController`), `[Authorize]`:
  - `GET /api/v1/me/shifts?from=&to=` → `GetMyShiftsQuery` (employee resolved from `sub`).
  - `POST /api/v1/me/shifts/{shiftId}/start` (body `{ occurrenceStartUtc }`) → `StartQualityCycleFormCommand`.
  - `POST /api/v1/me/quality-cycle/{formId}/end` (body items/photos/notes) → `EndQualityCycleFormCommand`.

### Task 4: Frontend — employee My Shifts page + inline form

- New route `site/src/app/me/shifts/page.tsx` (client, Keycloak-authenticated, employee role).
- Start/End buttons per occurrence; inline checklist form (reuse `@/ds/*` components + checklist renderer from `QualityCyclePublicClient`).
- `site/src/lib/qualityCycleApi.ts` + new `meApi.ts`: `myShifts(from,to)`, `startShift(shiftId, occurrenceStartUtc)`, `endForm(formId, payload)`.

### Task 5: Report — actual hours

- Modify `IQualityCyclePdfGenerator` / `QualityCycleReportSummary.Compute` to use `EndedAtUtc - StartedAtUtc` (actual) fallback `ShiftOccurrenceEndUtc - ShiftOccurrenceUtc` (planned).
- Keep monthly PDF per employee/branch/shift (existing `GetQualityCycleSummaryPdfQuery`).
- Admin report controls on `site/src/app/admin/quality-cycle/page.tsx` (employee/branch/shift + month).

### Task 6: Cleanup — remove email dispatch

- Delete `QualityCycleDispatcher` + `IQualityCycleDispatcher` + `DispatchQualityCycleFormsCommand`/handler + on-assignment dispatch in `ShiftApp.cs` (old Task 2/5).
- Remove `IQualityCycleRepository`/`IEmailSender` deps from `ShiftApp.cs`; revert constructor.
- Revert dispatcher DI registration in `Program.cs`.

---

## Self-Review Notes

- Clock fields live on `QualityCycleForm` (per shift/employee/occurrence record) — reused, no new entity.
- Report unchanged in shape; only hours source becomes actual times.
- Removed: background service, email, backfill, dispatcher. Added: employee portal + Start/End endpoints + inline form.
