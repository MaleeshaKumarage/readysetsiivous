import { test, expect } from '@playwright/test';
import { setupScheduler, column, panel, DAY, PAST } from './scheduler-fixture';

// The Scheduler joins the employee schedule and the shift calendar, laid out as docs/design/office-portal/Scheduler.dc.html.

test('the page matches the design: header, controls, columns, visit cards and footer note', async ({ page }) => {
  await setupScheduler(page);
  await expect(page.getByText(/^Day view, Tue 15 Jan\. Assigned visits only; unassigned work is in the Work queue\. Drag a visit to change its time or assignee\./)).toBeVisible();
  await expect(page.getByRole('link', { name: 'Work queue', exact: true }).last()).toHaveAttribute('href', '/admin/work-queue/');
  for (const name of ['Previous', 'Today', 'Next']) await expect(page.getByRole('button', { name, exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Employees' })).toHaveValue('all');
  await expect(page.getByRole('group', { name: 'View' }).getByRole('button', { name: 'Day' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByLabel('Show 24 h')).not.toBeChecked();
  await expect(page.getByLabel('Show weekends')).toHaveCount(0);
  // No date picker, no refresh, no review filter: the design removed them.
  await expect(page.getByRole('button', { name: /Refresh/ })).toHaveCount(0);
  await expect(page.getByLabel(/^Date/)).toHaveCount(0);
  await expect(page.getByText(/need review/i)).toHaveCount(0);

  const grid = page.getByRole('region', { name: 'Schedule grid' });
  await expect(grid.getByText('08:00–16:00 · 1 visit')).toBeVisible();
  await expect(grid.getByText('07:00–15:00 · 2 visits')).toBeVisible();
  await expect(grid.getByText('No hours set · 2 visits')).toBeVisible();
  await expect(column(page, 'Anna Korhonen').getByRole('button', { name: 'Espoo, weekly clean, 09:00–12:00, In progress, needs review' })).toBeVisible();
  await expect(column(page, 'Ville Rantanen').getByRole('button', { name: 'Hietaniemi office, 10:00–14:00, Scheduled, overlaps another visit' })).toHaveClass(/blk-over/);
  await expect(column(page, 'Minna Salo').getByRole('button', { name: 'Espoo, weekly clean, 07:30–09:30, Needs action, needs review' }).getByText('Needs review')).toBeVisible();
  // A cancelled visit stays on its assignee's column, struck through.
  await expect(column(page, 'Minna Salo').getByRole('button', { name: 'Helsinki showroom, 15:00–16:00, Cancelled' })).toHaveClass(/blk-cancelled/);
  // A short card still shows its title first.
  await expect(column(page, 'Minna Salo').getByRole('button', { name: /^Helsinki showroom/ }).getByText('Helsinki showroom')).toBeVisible();
  expect(await column(page, 'Minna Salo').getByRole('button', { name: /^Helsinki showroom/ }).locator('strong').evaluate(el => el.getBoundingClientRect().height)).toBeGreaterThan(10);
  await expect(page.getByText('Visits in progress, done or cancelled cannot be moved. Gold outline: overlaps another visit for the same assignee.')).toBeVisible();
});

test('clicking a visit opens the side panel with its details; Escape and × close it', async ({ page }) => {
  await setupScheduler(page);
  await column(page, 'Ville Rantanen').getByRole('button', { name: /^Hietaniemi office/ }).click();
  const details = panel(page);
  await expect(details.getByRole('heading', { name: 'Hietaniemi office' })).toBeVisible();
  await expect(details.getByText('Hietaniemi · Hietaniemi office Oy')).toBeVisible();
  for (const chip of ['Scheduled', 'Overlap']) await expect(details.getByText(chip, { exact: true })).toBeVisible();
  for (const [term, value] of [['Visit', 'Tue 15 Jan, 10:00–14:00'], ['Number', 'Visit 1'], ['Assignee', 'Ville Rantanen'], ['Duration', '4 h'], ['Actual', 'Not recorded'], ['Checklist', '0 of 9 done'], ['Notes', 'No notes']] as const) {
    await expect(details.getByRole('definition').filter({ hasText: value }).first()).toBeVisible();
    await expect(details.getByRole('term').filter({ hasText: term }).first()).toBeVisible();
  }
  await expect(details.getByText('This visit overlaps another visit by the same assignee. Move or reassign one of them.')).toBeVisible();
  // Today's visit: work times and photos are shown but wait until the day has passed.
  await expect(details.getByRole('button', { name: 'Record start and end' })).toBeDisabled();
  await expect(details.getByRole('button', { name: 'Add photos' })).toBeDisabled();
  await expect(details.getByText('Start and end times and photos can be added once the visit day has passed, if the assignee did not record them.')).toBeVisible();
  // No move or reassign controls in the panel: that is done by dragging.
  await expect(details.getByRole('button', { name: /15 min/ })).toHaveCount(0);
  await expect(details.getByLabel(/Move to/)).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(details).toHaveCount(0);
  await column(page, 'Minna Salo').getByRole('button', { name: /^Espoo, weekly clean/ }).click();
  await expect(details.getByText('Missing completion', { exact: true })).toBeVisible();
  await expect(details.getByText('No start or end recorded')).toBeVisible();
  await expect(details.getByText('Not started. Call the assignee.')).toBeVisible();
  await details.getByRole('button', { name: 'Close visit details' }).click();
  await expect(details).toHaveCount(0);
});

test('a locked visit says why it cannot be moved', async ({ page }) => {
  await setupScheduler(page);
  await column(page, 'Minna Salo').getByRole('button', { name: /^Helsinki showroom/ }).click();
  await expect(panel(page).getByText('Status: Cancelled. It cannot be moved or reassigned now.')).toBeVisible();
  await expect(panel(page).getByText('Cancelled visits have no work times or photos.')).toBeVisible();
  await panel(page).getByRole('button', { name: 'Close visit details' }).click();
  await column(page, 'Anna Korhonen').getByRole('button', { name: /^Espoo, weekly clean/ }).click();
  await expect(panel(page).getByText('Status: In progress. It cannot be moved or reassigned now.')).toBeVisible();
  await expect(panel(page).getByText('09:02 – Not ended')).toBeVisible();
});

test('Previous, Today and Next move by a day and load the calendar for the shown day', async ({ page }) => {
  const state = await setupScheduler(page);
  await expect.poll(() => state.calendarCalls).toContain(DAY);
  await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(page.getByText(/^Day view, Mon 14 Jan\./)).toBeVisible();
  await expect.poll(() => state.calendarCalls.at(-1)).toBe(PAST);
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByText(/^Day view, Wed 16 Jan\./)).toBeVisible();
  await page.getByRole('button', { name: 'Today', exact: true }).click();
  const today = new Date().toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
  await expect(page.getByText(new RegExp(`^Day view, ${today}\\.`))).toBeVisible();
});

test('the Employees dropdown shows one employee or everyone', async ({ page }) => {
  await setupScheduler(page);
  await page.getByRole('combobox', { name: 'Employees' }).selectOption({ label: 'Ville Rantanen' });
  await expect(column(page, 'Ville Rantanen')).toBeVisible();
  await expect(column(page, 'Anna Korhonen')).toHaveCount(0);
  await page.getByRole('combobox', { name: 'Employees' }).selectOption('all');
  await expect(column(page, 'Anna Korhonen')).toBeVisible();
});

test('Week view shows one employee Monday to Friday, with weekends and 24 h on request', async ({ page }) => {
  await setupScheduler(page);
  await page.getByRole('group', { name: 'View' }).getByRole('button', { name: 'Week' }).click();
  await expect(page.getByText(/^Week of 14 January for Anna Korhonen\./)).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Employee', exact: true })).toHaveValue('anna');
  await expect(column(page, 'Anna Korhonen', PAST).getByRole('button', { name: /^Kallio, Lindqvist flat, 08:00–11:00, Done/ })).toBeVisible();
  await expect(page.getByRole('group', { name: /^Anna Korhonen, 2030-01-1[89]$/ })).toHaveCount(1);
  await expect(page.getByRole('group', { name: 'Anna Korhonen, 2030-01-20' })).toHaveCount(0);
  await page.getByLabel('Show weekends').check();
  await expect(page.getByRole('group', { name: 'Anna Korhonen, 2030-01-20' })).toBeVisible();
  await page.getByRole('combobox', { name: 'Employee', exact: true }).selectOption({ label: 'Ville Rantanen' });
  await expect(column(page, 'Ville Rantanen').getByRole('button', { name: /^Hietaniemi office/ })).toBeVisible();
  await page.getByLabel('Show 24 h').check();
  await expect(page.getByRole('region', { name: 'Schedule grid' }).getByText('00:00', { exact: true })).toBeVisible();
  await page.getByRole('group', { name: 'View' }).getByRole('button', { name: 'Day' }).click();
  await expect(page.getByRole('combobox', { name: 'Employees' })).toBeVisible();
});

test('a past visit records start and end times with a reason, then offers Edit work times', async ({ page }) => {
  const state = await setupScheduler(page, { query: `?date=${PAST}` });
  await column(page, 'Anna Korhonen', PAST).getByRole('button', { name: /^Kallio, Lindqvist flat/ }).click();
  await expect(panel(page).getByText('08:05 – 11:02 · 2.95 hours')).toBeVisible();
  await expect(panel(page).getByText('12 of 12 done')).toBeVisible();
  await panel(page).getByRole('button', { name: 'Edit work times' }).click();
  const dialog = page.getByRole('dialog', { name: 'Edit work times' });
  await expect(dialog.getByText('Anna Korhonen · Kallio, Lindqvist flat')).toBeVisible();
  await expect(dialog.getByText('Scheduled: Mon 14 Jan, 08:00 – 11:00')).toBeVisible();
  await expect(dialog.getByLabel('Actual start')).toHaveValue(`${PAST}T08:05`);
  await dialog.getByLabel('Actual start').selectOption(`${PAST}T08:15`);
  await dialog.getByLabel('Actual end').selectOption(`${PAST}T11:00`);
  await expect(dialog.getByText('Work duration: 2.75 hours')).toBeVisible();
  await dialog.getByRole('button', { name: 'Save work times' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Enter both times and a reason.');
  await dialog.getByLabel('Actual end').selectOption(`${PAST}T08:00`);
  await dialog.getByLabel('Reason').fill('Timesheet received');
  await dialog.getByRole('button', { name: 'Save work times' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('End must be after start.');
  await dialog.getByLabel('Actual end').selectOption(`${PAST}T11:00`);
  await dialog.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('status')).toContainText('Work times saved.');
  expect(state.saved.find(item => item.path.endsWith('/calendar/work-time'))!.body).toMatchObject({ shiftId: 'kallio', employeeId: 'anna',
    occurrenceStartUtc: `${PAST}T06:00:00Z`, actualStartLocal: `${PAST}T08:15`, actualEndLocal: `${PAST}T11:00`, reason: 'Timesheet received', version: 'v-1' });
  await expect(panel(page).getByText('08:15 – 11:00 · 2.75 hours')).toBeVisible();
});

test('a refused work-time save keeps the dialog open with the server reason', async ({ page }) => {
  const state = await setupScheduler(page, { query: `?date=${PAST}` });
  state.failWork = 409;
  await column(page, 'Anna Korhonen', PAST).getByRole('button', { name: /^Kallio, Lindqvist flat/ }).click();
  await panel(page).getByRole('button', { name: 'Edit work times' }).click();
  const dialog = page.getByRole('dialog', { name: 'Edit work times' });
  await dialog.getByLabel('Reason').fill('Timesheet');
  await dialog.getByRole('button', { name: 'Save work times' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Work times changed. Reload the calendar before saving.');
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(dialog).toHaveCount(0);
});

test('Add photos needs a note per photo and a reason, then saves them to the visit', async ({ page }) => {
  const state = await setupScheduler(page, { query: `?date=${PAST}` });
  await column(page, 'Anna Korhonen', PAST).getByRole('button', { name: /^Kallio, Lindqvist flat/ }).click();
  await panel(page).getByRole('button', { name: 'Add photos' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add photos' });
  await expect(dialog.getByText('Anna Korhonen · Kallio, Lindqvist flat')).toBeVisible();
  await dialog.getByRole('button', { name: 'Save photos' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Choose at least one photo.');
  // A 1×1 PNG stands in for a photo the employee sent.
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');
  const chooser = page.waitForEvent('filechooser');
  await dialog.getByRole('button', { name: 'Add a photo' }).click();
  await (await chooser).setFiles({ name: 'kitchen.png', mimeType: 'image/png', buffer: png });
  await expect(dialog.getByRole('img', { name: 'Photo 1' })).toBeVisible();
  await dialog.getByLabel('Note for photo 1').fill('Kitchen after cleaning');
  await dialog.getByRole('button', { name: 'Save photos' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Enter a reason.');
  await dialog.getByLabel('Reason').fill('Sent by WhatsApp');
  await dialog.getByRole('button', { name: 'Save photos' }).click();
  await expect(page.getByRole('status')).toContainText('Photos added to the visit.');
  const saved = state.saved.find(item => item.path.endsWith('/calendar/work-photos'))!.body;
  expect(saved).toMatchObject({ shiftId: 'kallio', employeeId: 'anna', reason: 'Sent by WhatsApp', version: 'v-1' });
  expect(saved.photos).toHaveLength(1);
  expect(saved.photos[0].note).toBe('Kitchen after cleaning');
});

test('+ Visit schedules a work order visit for that assignee and day', async ({ page }) => {
  const state = await setupScheduler(page);
  await page.getByRole('button', { name: 'Add visit for Anna Korhonen' }).click();
  const dialog = page.getByRole('dialog', { name: 'New visit' });
  await expect(dialog.getByText('Anna Korhonen, Tue 15 Jan')).toBeVisible();
  // Only active work orders, the ones visits are added to.
  await expect(dialog.getByLabel('Work order').locator('option')).toHaveText(['Choose a work order', 'Kallio, Lindqvist flat', 'Helsinki showroom']);
  await dialog.getByRole('button', { name: 'Save visit' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Choose a work order.');
  await dialog.getByLabel('Work order').selectOption({ label: 'Helsinki showroom' });
  await dialog.getByLabel('Start').selectOption('10:30');
  await dialog.getByLabel('Length').selectOption({ label: '2 h' });
  await expect(dialog.getByText('Ends at 12:30.')).toBeVisible();
  await dialog.getByRole('button', { name: 'Save visit' }).click();
  await expect(page.getByRole('status')).toContainText('Scheduled Helsinki showroom for Anna Korhonen on Tue 15 Jan, 10:30–12:30.');
  expect(state.saved.find(item => item.path.endsWith('/branch-visits'))!.body).toMatchObject({ branchId: 'helsinki-branch', fromDate: DAY, toDate: DAY,
    startTime: '10:30', endTime: '12:30', employeeId: 'anna', useDefaultEmployee: false, cleaningType: 'Normal' });
  await page.getByRole('status').getByRole('button', { name: 'Dismiss message' }).click();
  await expect(page.getByRole('status')).toHaveCount(0);
});

test('a refused new visit keeps the dialog open with the reason; Cancel closes it', async ({ page }) => {
  const state = await setupScheduler(page);
  state.failVisit = true;
  await page.getByRole('button', { name: 'Add visit for Anna Korhonen' }).click();
  const dialog = page.getByRole('dialog', { name: 'New visit' });
  await dialog.getByLabel('Work order').selectOption({ label: 'Kallio, Lindqvist flat' });
  await dialog.getByRole('button', { name: 'Save visit' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Anna Korhonen already has a visit at this time.');
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('button', { name: 'Add visit for Ville Rantanen' }).click();
  await page.getByRole('dialog', { name: 'New visit' }).getByRole('button', { name: 'Close' }).click();
  await expect(page.getByRole('dialog', { name: 'New visit' })).toHaveCount(0);
});

test('without shift calendar access the work-time actions explain how to get it and nothing is requested', async ({ page }) => {
  const state = await setupScheduler(page, { calendar: false, query: `?date=${PAST}` });
  await column(page, 'Anna Korhonen', PAST).getByRole('button', { name: /^Kallio, Lindqvist flat/ }).click();
  await expect(panel(page).getByRole('button', { name: 'Record start and end' })).toBeDisabled();
  await expect(panel(page).getByRole('button', { name: 'Add photos' })).toBeDisabled();
  await expect(panel(page).getByText('Shift calendar access is needed to record work times and add photos. Ask an owner to enable it, then reload.')).toBeVisible();
  expect(state.calendarCalls).toHaveLength(0);
});

test('the Scheduler navigation item opens the page, and old calendar links land on it', async ({ page }) => {
  await setupScheduler(page);
  await page.goto('/admin/');
  await page.getByRole('button', { name: 'Toggle navigation' }).click({ timeout: 3000 }).catch(() => undefined);
  await expect(page.getByRole('link', { name: 'Shift calendar', exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Employee schedule', exact: true })).toHaveCount(0);
  await page.getByRole('link', { name: 'Scheduler', exact: true }).first().click();
  await expect(page).toHaveURL(/\/admin\/shifts\/scheduler\/?$/);
  await expect(page.getByRole('heading', { name: 'Scheduler', exact: true })).toBeVisible();
  await page.goto(`/admin/shifts/calendar/?date=${PAST}`);
  await expect(page).toHaveURL(new RegExp(`/admin/shifts/scheduler/\\?date=${PAST}$`));
  await expect(page.getByText(/^Day view, Mon 14 Jan\./)).toBeVisible();
  await page.goto(`/admin/shifts/employee-schedule/?employee=ville&date=${DAY}`);
  await expect(page).toHaveURL(new RegExp(`/admin/shifts/scheduler/\\?date=${DAY}&employee=ville$`));
  await expect(page.getByText(/^Week of 14 January for Ville Rantanen\./)).toBeVisible();
});

test('in week view + Visit schedules on that column’s day for the chosen employee', async ({ page }) => {
  const state = await setupScheduler(page);
  await page.getByRole('button', { name: 'Week', exact: true }).click();
  await page.getByRole('button', { name: 'Add visit for Anna Korhonen on Thu 17 Jan' }).click();
  const dialog = page.getByRole('dialog', { name: 'New visit' });
  await expect(dialog.getByText('Anna Korhonen, Thu 17 Jan')).toBeVisible();
  await dialog.getByLabel('Work order').selectOption({ label: 'Kallio, Lindqvist flat' });
  await dialog.getByRole('button', { name: 'Save visit' }).click();
  await expect(page.getByRole('status')).toContainText('Scheduled Kallio, Lindqvist flat for Anna Korhonen on Thu 17 Jan, 08:00–09:00.');
  expect(state.saved.find(item => item.path.endsWith('/branch-visits'))!.body).toMatchObject({ branchId: 'kallio-branch', fromDate: '2030-01-17', toDate: '2030-01-17', employeeId: 'anna' });
});

test('dialogs take focus, close with Escape and give focus back', async ({ page }) => {
  await setupScheduler(page, { query: `?date=${PAST}` });
  const add = page.getByRole('button', { name: 'Add visit for Anna Korhonen' });
  await add.click();
  await expect(page.getByRole('dialog', { name: 'New visit' }).getByLabel('Work order')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(add).toBeFocused();
  await column(page, 'Anna Korhonen', PAST).getByRole('button', { name: /^Kallio, Lindqvist flat/ }).click();
  const photos = panel(page).getByRole('button', { name: 'Add photos' });
  await photos.click();
  await expect(page.getByRole('dialog', { name: 'Add photos' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(photos).toBeFocused();
  await photos.click();
  await page.getByRole('dialog', { name: 'Add photos' }).getByRole('button', { name: 'Close photos' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await photos.click();
  await page.getByRole('dialog', { name: 'Add photos' }).getByRole('button', { name: 'Cancel' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  // The panel is still open after the dialogs close.
  await expect(panel(page)).toBeVisible();
});

test('the page refreshes on its own when the window regains focus', async ({ page }) => {
  const state = await setupScheduler(page);
  await expect.poll(() => state.scheduleCalls.length).toBeGreaterThan(0);
  const before = state.scheduleCalls.length;
  const calendarBefore = state.calendarCalls.length;
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect.poll(() => state.scheduleCalls.length).toBeGreaterThan(before);
  await expect.poll(() => state.calendarCalls.length).toBeGreaterThan(calendarBefore);
});

test('the work order list is read once and only lists active work orders', async ({ page }) => {
  await setupScheduler(page);
  const reads: string[] = [];
  page.on('request', request => { if (/\/api\/v1\/admin\/shifts$/.test(new URL(request.url()).pathname) && request.method() === 'GET') reads.push(request.url()); });
  await page.getByRole('button', { name: 'Add visit for Ville Rantanen' }).click();
  await expect(page.getByRole('dialog', { name: 'New visit' }).getByLabel('Work order').locator('option')).toHaveCount(3);
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Add visit for Minna Salo' }).click();
  await expect(page.getByRole('dialog', { name: 'New visit' }).getByText('Minna Salo, Tue 15 Jan')).toBeVisible();
  expect(reads).toHaveLength(1);
});

test('overlapping visits shrink to share their column; the column keeps the same width as the others', async ({ page }) => {
  await setupScheduler(page, { thirdOverlap: true });
  const ville = column(page, 'Ville Rantanen');
  const cards = ville.getByRole('button', { name: /overlaps another visit|^Third overlapping/ });
  await expect(cards).toHaveCount(3);
  const lane = (await ville.boundingBox())!;
  const other = (await column(page, 'Anna Korhonen').boundingBox())!;
  // Every column is the same width, however many visits overlap in it.
  expect(Math.abs(lane.width - other.width)).toBeLessThanOrEqual(1);
  const boxes = await Promise.all([0, 1, 2].map(async index => (await cards.nth(index).boundingBox())!));
  for (const box of boxes) {
    // Each card stays inside its column and takes about a third of it.
    expect(box.x).toBeGreaterThanOrEqual(lane.x);
    expect(box.x + box.width).toBeLessThanOrEqual(lane.x + lane.width + 0.5);
    expect(box.width).toBeLessThan(lane.width / 3);
  }
  // Side by side: no two cards cover the same horizontal space.
  const sorted = [...boxes].sort((a, b) => a.x - b.x);
  for (let i = 1; i < sorted.length; i++) expect(sorted[i].x).toBeGreaterThanOrEqual(sorted[i - 1].x + sorted[i - 1].width - 0.5);
  // A long name is cut with an ellipsis instead of widening or spilling out of the card.
  const title = ville.getByRole('button', { name: /^Third overlapping/ }).locator('strong');
  expect(await title.evaluate(el => getComputedStyle(el).textOverflow)).toBe('ellipsis');
  expect(await title.evaluate(el => el.scrollWidth > el.clientWidth)).toBe(true);
  // Times are never cut: they wrap inside the narrow card instead.
  const time = ville.getByRole('button', { name: /^Hietaniemi office/ }).getByText('10:00–14:00');
  expect(await time.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
});
