# Quality Cycle Auto-Dispatch at Shift End Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically dispatch Quality Cycle fill-in forms + emails to assigned employees when their shift occurrence ends, with backdating for in-progress shifts and a monthly summary PDF report per employee/branch/shift.

**Architecture:** An in-app `BackgroundService` runs every 5 minutes, finds shift occurrences whose `EndUtc` just passed, and for each assigned employee creates a `QualityCycleForm` (idempotent) and emails the fill link. Backdating backfills past occurrences when a template is assigned to an in-progress shift. The report generator aggregates submitted forms into a monthly PDF.

**Tech Stack:** ASP.NET Core 8, Marten, MediatR, FluentValidation, xUnit + Moq, QuestPDF (existing `IQualityCyclePdfGenerator`), Resend (`IEmailSender`), Next.js + Mantine (frontend), Playwright (e2e).

**Spec:** `docs/superpowers/specs/2026-10-10-quality-cycle-auto-dispatch-design.md`

## Global Constraints

- .NET 8 (`net8.0`), C# — nullable enabled.
- Marten documents inherit `BaseDocument`; optimistic concurrency on.
- Multi-tenant: everything scoped by `ITenantContext.TenantId` (dispatcher + background service must resolve tenant).
- Errors return RFC 7807 problem details; 409 for conflicts, 404 for missing entities.
- Frontend: Mantine v7, English-only admin (`/admin`), camelCase JSON (API is case-insensitive).
- Money is decimal; UTC in storage, Europe/Helsinki at edges.
- Email failures must not roll back form creation or assignment (log + continue).

---

### Task 1: Add `ShiftOccurrenceEndUtc` to the form (duration for report)

**Files:**
- Modify: `api/CleaningSuite.Domain/QualityCycle/QualityCycleForm.cs`
- Modify: `api/CleaningSuite.Application/QualityCycle/QualityCycleApp.cs` (DTO + map)
- Test: `api/CleaningSuite.Tests/QualityCycle/QualityCycleFormTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `QualityCycleForm.ShiftOccurrenceEndUtc` (`DateTime`); `QualityCycleForm.Create(...)` gains a `DateTime shiftOccurrenceEndUtc` parameter; `QualityCycleFormDto.ShiftOccurrenceEndUtc`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Create_StoresOccurrenceEndUtc()
{
    var start = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    var end = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
    var form = QualityCycleForm.Create(
        Guid.NewGuid(), "Daily clean", Guid.NewGuid(), "Test User",
        Guid.NewGuid(), "Template", start, end, new List<string> { "Dust" });
    Assert.Equal(start, form.ShiftOccurrenceUtc);
    Assert.Equal(end, form.ShiftOccurrenceEndUtc);
}
```

- [ ] **Step 2: Run test, verify fail** — `dotnet test api/CleaningSuite.Tests -c Release --filter QualityCycleFormTests` — FAIL (no `ShiftOccurrenceEndUtc`).

- [ ] **Step 3: Implement**

In `QualityCycleForm.cs`, add `public DateTime ShiftOccurrenceEndUtc { get; set; }` after `ShiftOccurrenceUtc`, add the `DateTime shiftOccurrenceEndUtc` param to `Create(...)` (after `shiftOccurrenceUtc`), and set `ShiftOccurrenceEndUtc = shiftOccurrenceEndUtc;`. Update the 1 existing `Create` call site in `ShiftApp.cs` (the `DispatchQualityCycleFormAsync` helper) to pass `occurrence.EndUtc`.

- [ ] **Step 4: Run test, verify pass** — same filter — PASS.

- [ ] **Step 5: Commit** — `git add` the three files; `git commit -m "feat(qc): store occurrence end time on QualityCycleForm"`.

---

### Task 2: Extract `IQualityCycleDispatcher` service

**Files:**
- Create: `api/CleaningSuite.Application/QualityCycle/IQualityCycleDispatcher.cs`
- Create: `api/CleaningSuite.Application/QualityCycle/QualityCycleDispatcher.cs`
- Test: `api/CleaningSuite.Tests/QualityCycle/QualityCycleDispatcherTests.cs`

**Interfaces:**
- Consumes: `IQualityCycleRepository`, `IEmailSender`, `IShiftRepository`, `IEmployeeRepository`, `ShiftScheduleCalculator`.
- Produces: `Task<int> DispatchAsync(Shift shift, CancellationToken ct)` — dispatches forms for ALL assigned employees across all future occurrences of `shift` (idempotent via `GetFormByShiftOccurrenceAsync`), returns count.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Dispatch_SkipsExistingForm()
{
    var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily", _dailySchedule, null, null, null);
    var emp = new Employee { Id = Guid.NewGuid(), Email = "e@x.fi", FirstName = "A", LastName = "B", IsActive = true };
    var template = QualityCycleTemplate.Create("T", new List<string> { "Item1" }, null, null, null);
    shift.QualityCycleTemplateId = template.Id;

    _shiftRepo.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
    _qcRepo.Setup(r => r.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
    _shiftRepo.Setup(r => r.ListAssignmentsByShiftAsync(shift.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ShiftAssignment> { ShiftAssignment.Create(shift.Id, emp.Id, null) });
    _empRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
    _qcRepo.Setup(r => r.GetFormByShiftOccurrenceAsync(shift.Id, emp.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new QualityCycleForm());

    var count = await _dispatcher.DispatchAsync(shift, CancellationToken.None);

    Assert.Equal(0, count);
    _emailSender.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] **Step 2: Run test, verify fail** — FAIL (`_dispatcher` not defined).

- [ ] **Step 3: Implement** — port the loop body of `DispatchQualityCycleFormsCommandHandler` (get template, generate occurrences, per assigned employee create form + email) into `QualityCycleDispatcher.DispatchAsync(shift, ct)`. Register `AddScoped<IQualityCycleDispatcher, QualityCycleDispatcher>()` in `Program.cs`.

- [ ] **Step 4: Run test, verify pass** — PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(qc): extract IQualityCycleDispatcher service"`.

---

### Task 3: `QualityCycleBackgroundService` (5-min auto-dispatch at occurrence end)

**Files:**
- Create: `api/CleaningSuite.Infrastructure/QualityCycle/QualityCycleBackgroundService.cs`
- Modify: `api/CleaningSuite.Api/Program.cs` (register hosted service)
- Test: `api/CleaningSuite.Tests/QualityCycle/QualityCycleBackgroundServiceTests.cs`

**Interfaces:**
- Consumes: `IServiceScopeFactory`, `IQualityCycleDispatcher`, `IShiftRepository` (via scope), `ITenantContext`.
- Produces: `BackgroundService` registered via `AddHostedService<QualityCycleBackgroundService>()`.

- [ ] **Step 1: Write the failing test** (test the "which occurrences are due" predicate)

```csharp
[Fact]
public void ShouldDispatch_OccurrenceEndInLastFiveMinutes()
{
    var now = new DateTime(2026, 10, 1, 10, 5, 0, DateTimeKind.Utc);
    var occ = new ShiftOccurrence(new DateTime(2026,10,1,8,0,0,DateTimeKind.Utc), new DateTime(2026,10,1,10,2,0,DateTimeKind.Utc));
    Assert.True(QualityCycleBackgroundService.IsDue(occ, now, TimeSpan.FromMinutes(5)));
}

[Fact]
public void ShouldDispatch_False_WhenEndTooOld()
{
    var now = new DateTime(2026, 10, 1, 10, 30, 0, DateTimeKind.Utc);
    var occ = new ShiftOccurrence(new DateTime(2026,10,1,8,0,0,DateTimeKind.Utc), new DateTime(2026,10,1,10,2,0,DateTimeKind.Utc));
    Assert.False(QualityCycleBackgroundService.IsDue(occ, now, TimeSpan.FromMinutes(5)));
}
```

- [ ] **Step 2: Run test, verify fail** — FAIL (class not defined).

- [ ] **Step 3: Implement** — `QualityCycleBackgroundService` with a `PeriodicTimer` (5 min). Each tick: resolve a scope, list shifts with `IsActive && QualityCycleTemplateId != null`, for each shift generate occurrences in `[now - 6h, now + 6h]`, find due occurrences (`EndUtc` in `(now - interval, now]`), dispatch. `IsDue` is `internal static bool IsDue(ShiftOccurrence occ, DateTime now, TimeSpan window) => occ.EndUtc > now - window && occ.EndUtc <= now;`. Wrap each tick in try/catch.

- [ ] **Step 4: Run test, verify pass** — PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(qc): background service dispatches forms at occurrence end"`.

---

### Task 4: Backfill command (in-progress shift, from start/custom date)

**Files:**
- Modify: `api/CleaningSuite.Application/QualityCycle/QualityCycleApp.cs` (add command + handler)
- Modify: `api/CleaningSuite.Api/Controllers/QualityCycleAdminController.cs` (endpoint)
- Test: `api/CleaningSuite.Tests/QualityCycle/BackfillQualityCycleFormsCommandTests.cs`

**Interfaces:**
- Consumes: `IQualityCycleDispatcher`, `IShiftRepository`.
- Produces: `BackfillQualityCycleFormsCommand(Guid ShiftId, DateTime FromUtc) : IRequest<int>`; `POST /api/v1/admin/quality-cycle/backfill` with `{ shiftId, fromUtc }`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Backfill_DispatchesFormsWithinRange()
{
    var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily", _dailySchedule, null, null, null);
    shift.QualityCycleTemplateId = Guid.NewGuid();
    _shiftRepo.Setup(r => r.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);

    await new BackfillQualityCycleFormsCommandHandler(_shiftRepo.Object, _dispatcher.Object)
        .Handle(new BackfillQualityCycleFormsCommand(shift.Id, new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc)), CancellationToken.None);

    _dispatcher.Verify(d => d.DispatchAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 2: Run test, verify fail** — FAIL (command/handler not defined).

- [ ] **Step 3: Implement** — `BackfillQualityCycleFormsCommandHandler` loads the shift (404 if missing), then calls `_dispatcher.DispatchAsync(shift, ct)` (the dispatcher already backfills all occurrences from the shift's schedule within a window). For the custom-date window, pass the window through — extend `DispatchAsync` to accept an optional `DateTime? fromUtc` and only dispatch occurrences whose `StartUtc >= fromUtc`. Register the endpoint.

- [ ] **Step 4: Run test, verify pass** — PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(qc): backfill command for in-progress shifts"`.

---

### Task 5: Remove on-assignment dispatch

**Files:**
- Modify: `api/CleaningSuite.Application/Shifts/ShiftApp.cs` (remove `DispatchQualityCycleFormAsync` + its two calls + `IQualityCycleRepository`/`IEmailSender` deps)
- Modify: `api/CleaningSuite.Tests/Shifts/CreateShiftCommandHandlerTests.cs` (revert constructor to 2 args)

**Interfaces:**
- Consumes: nothing.
- Produces: `AssignEmployeeToShiftCommandHandler` constructor back to `(IShiftRepository, IEmployeeRepository, int maxConflictWindowDays = ...)`.

- [ ] **Step 1: Revert the two dispatch calls** in `Handle` (`await DispatchQualityCycleFormAsync(...)` lines) and delete the `DispatchQualityCycleFormAsync` helper.

- [ ] **Step 2: Revert constructor** — remove `IQualityCycleRepository`/`IEmailSender` fields + params; revert test constructor calls to `(_shiftRepoMock.Object, _employeeRepoMock.Object)` (and `maxConflictWindowDays: 0` variant).

- [ ] **Step 3: Run build + tests** — `dotnet test api/CleaningSuite.Backend.sln -c Release --no-build` — PASS.

- [ ] **Step 4: Commit** — `git commit -m "revert(qc): remove on-assignment dispatch (moved to shift-end)"`.

---

### Task 6: Enhanced monthly report generator

**Files:**
- Modify: `api/CleaningSuite.Application/QualityCycle/IQualityCyclePdfGenerator.cs` (new method signature)
- Modify: `api/CleaningSuite.Infrastructure/QualityCycle/QualityCyclePdfGenerator.cs`
- Modify: `api/CleaningSuite.Application/QualityCycle/QualityCycleApp.cs` (new query: per employee/branch)
- Test: `api/CleaningSuite.Tests/QualityCycle/QualityCyclePdfGeneratorTests.cs`

**Interfaces:**
- Consumes: `QualityCycleFormDto` (now with `ShiftOccurrenceEndUtc`).
- Produces: `byte[] GenerateMonthlySummaryPdf(ReportHeader header, IReadOnlyList<QualityCycleFormDto> forms)` where `ReportHeader` carries `Title`, `Subtitle` (employee/branch/shift + month). Add `GetQualityCycleReportPdfQuery(Guid? ShiftId, Guid? EmployeeId, Guid? BranchId, int Year, int Month) : IRequest<byte[]>`.

- [ ] **Step 1: Write the failing test** — a report builder that, given forms with known durations and checked items, produces a header with the correct visit count, hours sum, and per-item tick counts. Assert the pure aggregation method (`ComputeReportSummary`) returns `visits == N`, `totalHours == sum`, `itemCounts["Dust"] == M`.

- [ ] **Step 2: Run test, verify fail** — FAIL (method not defined).

- [ ] **Step 3: Implement** — a pure `QualityCycleReportSummary.Compute(forms)` returning `(int visits, double totalHours, IReadOnlyList<(string item, int count)> itemCounts)`. Compute `totalHours` from `ShiftOccurrenceEndUtc - ShiftOccurrenceUtc`; `itemCounts` from counting `Items.Where(IsChecked)`. Render into the PDF with QuestPDF: header (title/subtitle), visits + hours summary, per-day done list, item-count table, photos (embedded images) with notes. Add the per-employee/branch query handler that filters forms accordingly.

- [ ] **Step 4: Run test, verify pass** — PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(qc): enhanced monthly report with hours, item counts, photos"`.

---

### Task 7: Frontend — backdating prompt + report UI

**Files:**
- Modify: `site/src/app/admin/shifts/page.tsx` (backdating prompt on template assignment)
- Modify: `site/src/app/admin/quality-cycle/page.tsx` (report generation controls: employee/branch/shift + month)
- Modify: `site/src/lib/qualityCycleApi.ts` (backfill + report endpoints)

**Interfaces:**
- Consumes: `adminQualityCycle.backfill(shiftId, fromUtc)`, `adminQualityCycle.report({ shiftId?, employeeId?, branchId?, year, month })`.
- Produces: UI prompting "from today" / "from beginning" (with start-date or calendar) when assigning a template to an in-progress shift; report filters.

- [ ] **Step 1** — In the shift page, when the template selector changes and the shift has a past `ValidFrom`, open a modal asking "from today" or "from beginning"; "from beginning" reveals "start date" vs "custom date" (calendar `DateInput`). On save, call `adminQualityCycle.backfill(shiftId, fromUtc)` if "from beginning".

- [ ] **Step 2** — In the quality-cycle page, replace the removed "Dispatch" button area with report controls: month/year + a dimension select (Employee / Branch / Shift) + a "Generate report" button that downloads the PDF.

- [ ] **Step 3: Typecheck** — `cd site && npx tsc --noEmit -p tsconfig.json` — clean.

- [ ] **Step 4: Commit** — `git commit -m "feat(admin): backdating prompt + report generation UI"`.

---

### Task 8: Playwright e2e tests (edge cases)

**Files:**
- Modify: `site/e2e/quality-cycle.spec.ts` (or new `site/e2e/quality-cycle-dispatch.spec.ts`)
- Test: `site/e2e/quality-cycle-dispatch.spec.ts`

**Interfaces:**
- Consumes: the admin shift page, quality-cycle page, and public form.
- Produces: e2e coverage of the full dispatch + report flow.

Edge cases to cover (one test each):
1. Assign template to shift → mock a due occurrence → background service creates a form → employee public form shows the checklist.
2. Backfill "from beginning" with a custom date → past occurrences get forms + emails.
3. Backfill "from today" → no past forms created.
4. Duplicate dispatch (re-run) → no duplicate forms/emails (idempotent).
5. Report: generate per-employee → PDF downloads; per-branch; per-shift.
6. Report shows correct visits + hours + item counts for a month with mixed checked items.
7. Photo attachment → form submit with a photo → report includes the photo.

Each test uses Playwright route mocks for the API (like the existing `quality-cycle.spec.ts`), driving the real UI.

- [ ] **Step 1: Write the tests** (one file, the 7 cases above).

- [ ] **Step 2: Run** — `cd site && npx playwright test quality-cycle-dispatch.spec.ts --project=chromium-desktop` — pass all.

- [ ] **Step 3: Run full backend + e2e** — `dotnet test api/CleaningSuite.Backend.sln -c Release` + `npx playwright test` — all green.

- [ ] **Step 4: Commit** — `git commit -m "test(e2e): quality cycle auto-dispatch edge cases"`.

---

## Self-Review Notes

- Spec coverage: template assignment (existing), backdating (Task 4/7), auto-dispatch at end (Task 3), fill (existing), report (Task 6/7), removals (Task 5). All covered.
- Hours: `ShiftOccurrenceEndUtc` added (Task 1) so the report can sum durations without re-deriving from a possibly-changed schedule.
- Type consistency: `DispatchAsync(Shift, CancellationToken)` (Task 2) is reused by backfill (Task 4) with an optional `fromUtc` window — the exact overload is `Task<int> DispatchAsync(Shift shift, DateTime? fromUtc = null, CancellationToken ct = default)`.
