import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// The employee calendar: a day view across chosen employees, a week view for one employee,
// and visit details on selection. Times are Europe/Helsinki (UTC+2 in January).
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const MONDAY = '2030-01-14';
const TUESDAY = '2030-01-15';

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
  await page.goto(`/admin/shifts/employee-schedule/?date=${MONDAY}`);
  await expect(page.getByRole('heading', { name: 'Employee schedule' })).toBeVisible();
}

const details = (page: Page) => page.getByRole('complementary', { name: 'Visit details' });

test('selecting a visit shows its place, time, checklist and notes', async ({ page }) => {
  await setup(page);
  await page.getByRole('group', { name: `Neil Park, ${MONDAY}` }).getByRole('button', { name: /^Deep clean, 07:00–09:00, In progress$/ }).click();
  const panel = details(page);
  await expect(panel.getByRole('heading', { name: 'Deep clean' })).toBeVisible();
  await expect(panel.getByText('Quay Dental · Harbour Road')).toBeVisible();
  await expect(panel.getByText('Harbour Road 14, 00180 Helsinki')).toBeVisible();
  await expect(panel.getByText('In progress', { exact: true })).toBeVisible();
  await expect(panel.getByText('2 of 4 done')).toBeVisible();
  await expect(panel.getByRole('img', { name: '2 of 4 checklist items done' })).toBeVisible();
  await expect(panel.getByText('Alarm code is with reception.')).toBeVisible();
  await expect(panel.getByText('Neil Park', { exact: true })).toBeVisible();
  await panel.getByRole('button', { name: 'Close visit details' }).click();
  await expect(page.getByRole('heading', { name: 'Deep clean' })).toHaveCount(0);
});

test('each work state has its own badge and a visit without a checklist says so', async ({ page }) => {
  await setup(page);
  const neil = page.getByRole('group', { name: `Neil Park, ${MONDAY}` });
  await expect(neil.getByRole('button', { name: /^Window clean, 11:00–12:00, Needs action$/ })).toBeVisible();
  await expect(page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` }).getByRole('button', { name: /^Lawn service, 08:00–10:00, Completed$/ })).toBeVisible();
  await neil.getByRole('button', { name: /^Window clean/ }).click();
  await expect(details(page).getByText('Needs action', { exact: true })).toBeVisible();
  await expect(details(page).getByText('No checklist yet')).toBeVisible();
  // On phones the details sheet covers the list, so it is closed before picking another visit.
  await details(page).getByRole('button', { name: 'Close visit details' }).click();
  await page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` }).getByRole('button', { name: /^Lawn service/ }).click();
  await expect(details(page).getByText('Visit 12')).toBeVisible();
});

test('columns show each employee’s working hours for that day', async ({ page }) => {
  await setup(page);
  await expect(page.getByText('Available 07:00–15:00')).toBeVisible();
  await expect(page.getByText('No working hours set')).toBeVisible();
});

test('day view picks several employees from a dropdown list', async ({ page }) => {
  await setup(page);
  await expect(page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` })).toBeVisible();
  await page.getByRole('button', { name: /^Employees/ }).click();
  const list = page.getByRole('listbox', { name: 'Employees' });
  await expect(list).toHaveAttribute('aria-multiselectable', 'true');
  await list.getByRole('option', { name: 'Aiden Grey' }).click();
  // Several can be picked, so the list stays open after a pick.
  await expect(list).toBeVisible();
  await expect(list.getByRole('option', { name: 'Aiden Grey' })).toHaveAttribute('aria-selected', 'false');
  await expect(page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` })).toHaveCount(0);
  await expect(page.getByRole('group', { name: `Neil Park, ${MONDAY}` })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(list).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Employees Neil Park$/ })).toBeVisible();
});

test('week view shows one employee across the week, picked from a single-choice list', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: 'Week', exact: true }).click();
  await page.getByRole('button', { name: /^Employee Aiden Grey$/ }).click();
  const list = page.getByRole('listbox', { name: 'Employee' });
  await expect(list).toHaveAttribute('aria-multiselectable', 'false');
  await list.getByRole('option', { name: 'Neil Park' }).click();
  // A single choice closes the list.
  await expect(list).toHaveCount(0);
  await expect(page.getByRole('group', { name: `Neil Park, ${MONDAY}` })).toBeVisible();
  await expect(page.getByRole('group', { name: `Neil Park, ${TUESDAY}` }).getByRole('button', { name: /^Carpet clean, 09:00–10:00, Scheduled$/ })).toBeVisible();
  await expect(page.getByRole('group', { name: /^Aiden Grey/ })).toHaveCount(0);
  await expect(page.getByText('3 visits this week · Neil Park')).toBeVisible();
});

test('the shift calendar link is offered only to admins who can use the calendar, and opens the visit day', async ({ page }) => {
  await setup(page, { calendarAccess: false });
  await page.getByRole('group', { name: `Neil Park, ${MONDAY}` }).getByRole('button', { name: /^Deep clean/ }).click();
  await expect(details(page).getByRole('heading', { name: 'Deep clean' })).toBeVisible();
  await expect(details(page).getByRole('link', { name: 'Open in shift calendar' })).toHaveCount(0);

  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await setup(page, { calendarAccess: true });
  await page.getByRole('group', { name: `Neil Park, ${MONDAY}` }).getByRole('button', { name: /^Deep clean/ }).click();
  const link = details(page).getByRole('link', { name: 'Open in shift calendar' });
  await expect(link).toHaveAttribute('href', `/admin/shifts/calendar/?date=${MONDAY}`);

  const requested: string[] = [];
  await page.route(/\/api\/v1\/admin\/shifts\/calendar(?:\?|\/|$)/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    const date = new URL(route.request().url()).searchParams.get('date') ?? '';
    requested.push(date);
    return route.fulfill({ headers, json: { date: date || '2030-01-20', today: '2030-01-20', timeZoneId: 'Europe/Helsinki', occurrences: [] } });
  });
  await link.click();
  await expect(page).toHaveURL(/\/admin\/shifts\/calendar\/\?date=2030-01-14$/);
  await expect.poll(() => requested.includes(MONDAY)).toBe(true);
  await expect(page.getByLabel('Calendar date')).toHaveValue(MONDAY);
});

test('visit details stay closed until a visit is picked', async ({ page }) => {
  await setup(page);
  await expect(page.getByRole('group', { name: `Neil Park, ${MONDAY}` })).toBeVisible();
  await expect(details(page)).toHaveCount(0);
  await page.getByRole('group', { name: `Neil Park, ${MONDAY}` }).getByRole('button', { name: /^Deep clean/ }).click();
  await expect(details(page)).toBeVisible();
  await expect(details(page).getByRole('button', { name: 'Close visit details' })).toBeFocused();
});

test('on desktop, picking another visit while the panel is open switches the details', async ({ page, isMobile }) => {
  // On phones the details sheet covers the list, so another visit is picked after closing it.
  test.skip(isMobile, 'The phone sheet covers the visit list.');
  await setup(page);
  await page.getByRole('group', { name: `Neil Park, ${MONDAY}` }).getByRole('button', { name: /^Deep clean/ }).click();
  await expect(details(page).getByRole('heading', { name: 'Deep clean' })).toBeVisible();
  await page.getByRole('group', { name: `Aiden Grey, ${MONDAY}` }).getByRole('button', { name: /^Lawn service/ }).click();
  await expect(details(page).getByRole('heading', { name: 'Lawn service' })).toBeVisible();
  await expect(details(page).getByRole('heading', { name: 'Deep clean' })).toHaveCount(0);
  await expect(details(page)).toHaveCount(1);
});

test('on desktop, Escape closes the panel and returns focus to the visit', async ({ page, isMobile }) => {
  test.skip(isMobile, 'Phones have no hardware Escape key.');
  await setup(page);
  const card = page.getByRole('group', { name: `Neil Park, ${MONDAY}` }).getByRole('button', { name: /^Deep clean/ });
  await card.click();
  await expect(details(page)).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(details(page)).toHaveCount(0);
  await expect(card).toBeFocused();
});
