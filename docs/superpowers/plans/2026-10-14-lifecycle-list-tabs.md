# Lifecycle tabs implementation

1. Add shared typed status tab control and filter helper with accessible Active/Inactive names.
2. Load all authorized records: includeInactive for employees/shifts/manager shifts, paginate company management list fully; preserve existing helpers for selection/other consumers.
3. Apply list filtering and meaningful empty states across agreement/employee/service/company/branch/template/shift pages. Preserve action permissions and signed agreement state; ensure creation returns to Active and hidden assignment selection is cleared.
4. Update lifecycle-related Playwright tests and add mixed-status, signed preservation, pagination and empty-tab cases for desktop/mobile.
5. Document feature/test edge cases, type-check and run relevant suites; independent review and full CI before main merge and website deployment verification.

Parallel ownership: root shared tabs, helpers, services/companies/branches; agreements/employees task owns its two pages and focused tests; shifts/manager task owns its two pages and related tests; template task owns QC page and focused tests. Agents do not commit, push, merge or deploy.
