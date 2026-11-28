import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// The Scheduler's employee views: a day view across employees, a week view for one employee,
// and visit details on selection. Times are Europe/Helsinki (UTC+2 in January).
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const MONDAY = '2030-01-14';
const TUESDAY = '2030-01-15';
const SATURDAY = '2030-01-19';

async function setup(page: Page, options: { calendarAccess?: boolean } = {}) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: {
    realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], canUseShiftCalendar: options.calendarAccess ?? false, employee: null } }));
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/employee-schedule')) return route.fulfill({ headers, json: [
      { employeeId: 'neil', localDay: `${MONDAY}T00:00:00`, visits: [
        { shiftId: 'deep', shiftName: 'Deep clean', startUtc: `${MONDAY}T05:00:00Z`, endUtc: `${MONDAY}T07:00:00Z`,
          companyName: 'Quay Dental', branchName: 'Harbour Road', address: 'Harbour Road 14, 00180 Helsinki',
          workStatus: 'InProgress', visitNotes: 'Alarm code is with reception.', checklistDone: 2, checklistTotal: 4 },
        { shiftId: 'window', shiftName: 'Window clean', startUtc: `${MONDAY}T09:00:00Z`, endUtc: `${MONDAY}T10:00:00Z`,
          companyName: 'Quay Dental', branchName: 'Harbour Road', workStatus: 'NeedsAction', checklistDone: null, checklistTotal: null },
      ] },
      { employeeId: 'aiden', localDay: `${MONDAY}T00:00:00`, visits: [
        { shiftId: 'lawn', shiftName: 'Lawn service', startUtc: `${MONDAY}T06:00:00Z`, endUtc: `${MONDAY}T08:00:00Z`, workStatus: 'Completed', visitNumber: 12 },
      ] },
      { employeeId: 'neil', localDay: `${SATURDAY}T00:00:00`, visits: [
        { shiftId: 'weekend', shiftName: 'Saturday deep clean', startUtc: `${SATURDAY}T08:00:00Z`, endUtc: `${SATURDAY}T10:00:00Z`, workStatus: 'Scheduled' },
      ] },
      { employeeId: 'neil', localDay: `${TUESDAY}T00:00:00`, visits: [
        { shiftId: 'carpet', shiftName: 'Carpet clean', startUtc: `${TUESDAY}T07:00:00Z`, endUtc: `${TUESDAY}T08:00:00Z`, workStatus: 'Scheduled' },
      ] },
    ] });
    if (path.endsWith('/overlap-audit')) return route.fulfill({ headers, json: [] });
    if (path.endsWith('/employees')) return route.fulfill({ headers, json: [
      { id: 'aiden', firstName: 'Aiden', lastName: 'Grey', role: 'employee', isActive: true, defaultHours: {} },
      { id: 'neil', firstName: 'Neil', lastName: 'Park', role: 'employee', isActive: true,
        defaultHours: { Monday: { start: '07:00:00', end: '15:00:00' } } },
    ] });
    if (path.endsWith('/tenant')) return route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: [] });
  });
  await page.goto(`/admin/shifts/scheduler/?date=${MONDAY}`);
  await expect(page.getByRole('heading', { name: 'Scheduler', exact: true })).toBeVisible();
}

const details = (page: Page) => page.getByRole('complementary', { name: 'Visit details' });
const neil = (page: Page, day = MONDAY) => page.getByRole('group', { name: `Neil Park, ${day}` });

test('selecting a visit shows its place, time, checklist and notes', async ({ page }) => {
  await setup(page);
  await neil(page).getByRole('button', { name: /^Deep clean, 07:00–09:00, In progress$/ }).click();
  const panel = details(page);
  await expect(panel.getByRole('heading', { name: 'Deep clean' })).toBeVisible();
  await expect(panel.getByText('Harbour Road · Quay Dental')).toBeVisible();
  await expect(panel.getByText('In progress', { exact: true })).toBeVisible();
  await expect(panel.getByText('Mon 14 Jan, 07:00–09:00')).toBeVisible();
  await expect(panel.getByText('2 h', { exact: true })).toBeVisible();
  await expect(panel.getByText('2 of 4 done')).toBeVisible();
  await expect(panel.getByText('Alarm code is with reception.')).toBeVisible();
  await expect(panel.getByText('Neil Park', { exact: true })).toBeVisible();
  await panel.getByRole('button', { name: 'Close visit details' }).click();
  await expect(page.getByRole('heading', { name: 'Deep clean' })).toHaveCount(0);
});

test('each work state has its own label and a visit without a checklist says so', async ({ page }) => {
  await setup(page);
  await expect(neil(page).getByRole('button', { name: /^Window clean, 11:00–12:00, Needs action$/ })).toHaveClass(/blk-needs/);
  await expect(neil(page).getByRole('button', { name: /^Deep clean, 07:00–09:00, In progress$/ })).toHaveClass(/blk-progress/);
  await expect(page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` }).getByRole('button', { name: /^Lawn service, 08:00–10:00, Done$/ })).toHaveClass(/blk-done/);
  await neil(page).getByRole('button', { name: /^Window clean/ }).click();
  await expect(details(page).getByText('Needs action', { exact: true })).toBeVisible();
  await expect(details(page).getByText('No checklist yet')).toBeVisible();
  await expect(details(page).getByText('No notes')).toBeVisible();
  // The panel covers part of the grid, so it is closed before picking another visit.
  await details(page).getByRole('button', { name: 'Close visit details' }).click();
  await page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` }).getByRole('button', { name: /^Lawn service/ }).click();
  await expect(details(page).getByText('Visit 12')).toBeVisible();
});

test('columns show each employee’s working hours and visit count for that day', async ({ page }) => {
  await setup(page);
  await expect(page.getByText('07:00–15:00 · 2 visits')).toBeVisible();
  await expect(page.getByText('No hours set · 1 visit')).toBeVisible();
});

test('day view shows everyone or one employee from the Employees dropdown', async ({ page }) => {
  await setup(page);
  const employees = page.getByRole('combobox', { name: 'Employees' });
  await expect(employees.locator('option')).toHaveText(['All employees', 'Aiden Grey', 'Neil Park']);
  await expect(page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` })).toBeVisible();
  await employees.selectOption({ label: 'Neil Park' });
  await expect(page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` })).toHaveCount(0);
  await expect(neil(page)).toBeVisible();
});

test('week view shows one employee across the week, picked from the Employee dropdown', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: 'Week', exact: true }).click();
  const employee = page.getByRole('combobox', { name: 'Employee', exact: true });
  await expect(employee).toHaveValue('aiden');
  await employee.selectOption({ label: 'Neil Park' });
  await expect(neil(page)).toBeVisible();
  await expect(neil(page, TUESDAY).getByRole('button', { name: /^Carpet clean, 09:00–10:00, Scheduled$/ })).toBeVisible();
  await expect(page.getByRole('group', { name: /^Aiden Grey/ })).toHaveCount(0);
  await expect(page.getByText(/^Week of 14 January for Neil Park\./)).toBeVisible();
});

test('a day picked in the Employees dropdown opens that employee’s week', async ({ page }) => {
  await setup(page);
  await page.getByRole('combobox', { name: 'Employees' }).selectOption({ label: 'Neil Park' });
  await page.getByRole('button', { name: 'Week', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Employee', exact: true })).toHaveValue('neil');
  await page.getByRole('button', { name: 'Day', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Employees' })).toHaveValue('neil');
});

test('visit details stay closed until a visit is picked, then focus moves into them', async ({ page }) => {
  await setup(page);
  await expect(neil(page)).toBeVisible();
  await expect(details(page)).toHaveCount(0);
  await neil(page).getByRole('button', { name: /^Deep clean/ }).click();
  await expect(details(page)).toBeVisible();
  await expect(details(page).getByRole('button', { name: 'Close visit details' })).toBeFocused();
});

test('on desktop, picking another visit while the panel is open switches the details', async ({ page, isMobile }) => {
  // On phones the panel covers the grid, so another visit is picked after closing it.
  test.skip(isMobile, 'The phone panel covers the grid.');
  await setup(page);
  await neil(page).getByRole('button', { name: /^Deep clean/ }).click();
  await expect(details(page).getByRole('heading', { name: 'Deep clean' })).toBeVisible();
  await page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` }).getByRole('button', { name: /^Lawn service/ }).click();
  await expect(details(page).getByRole('heading', { name: 'Lawn service' })).toBeVisible();
  await expect(details(page).getByRole('heading', { name: 'Deep clean' })).toHaveCount(0);
  await expect(details(page)).toHaveCount(1);
});

test('Escape closes the panel and returns focus to the visit', async ({ page }) => {
  await setup(page);
  const card = neil(page).getByRole('button', { name: /^Deep clean/ });
  await card.click();
  await expect(details(page)).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(details(page)).toHaveCount(0);
  await expect(card).toBeFocused();
});

test('the grid shows 07:00–19:00, widened for earlier visits, and Show 24 h shows the whole day', async ({ page }) => {
  await setup(page);
  const grid = page.getByRole('region', { name: 'Schedule grid' });
  await expect(grid.getByText('07:00', { exact: true })).toBeVisible();
  await expect(grid.getByText('19:00', { exact: true })).toBeAttached();
  await expect(grid.getByText('06:00', { exact: true })).toHaveCount(0);
  await expect(grid.getByText('20:00', { exact: true })).toHaveCount(0);
  await page.getByLabel('Show 24 h').check();
  await expect(grid.getByText('00:00', { exact: true })).toBeVisible();
  await expect(grid.getByText('23:00', { exact: true })).toBeAttached();
  await page.getByLabel('Show 24 h').uncheck();
  await expect(grid.getByText('00:00', { exact: true })).toHaveCount(0);
});

test('week view shows Monday to Friday, with a notice and a tick box for weekend visits', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: 'Week', exact: true }).click();
  await page.getByRole('combobox', { name: 'Employee', exact: true }).selectOption({ label: 'Neil Park' });
  await expect(neil(page, '2030-01-18')).toBeVisible();
  await expect(neil(page, SATURDAY)).toHaveCount(0);
  const notice = page.getByText('1 weekend visit — tick Show weekends to see it.');
  await expect(notice).toBeVisible();
  await page.getByLabel('Show weekends').check();
  await expect(neil(page, SATURDAY).getByRole('button', { name: /^Saturday deep clean, 10:00–12:00, Scheduled$/ })).toBeVisible();
  await expect(neil(page, '2030-01-20')).toBeVisible();
  await expect(notice).toHaveCount(0);
});

test('day view has no weekend tick box', async ({ page }) => {
  await setup(page);
  await expect(page.getByLabel('Show weekends')).toHaveCount(0);
});
