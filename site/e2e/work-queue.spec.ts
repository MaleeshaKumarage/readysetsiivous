import { test, expect, type Page } from '@playwright/test';
import { setupWorkQueue, type WorkQueueState } from './work-queue-fixture';

// The Work queue as designed in docs/design/office-portal/Work-queue.dc.html: one list of typed items from every source,
// each handled in its side panel. Partner operations is gone; its work happens here.

const list = (page: Page) => page.getByRole('list', { name: 'Work waiting for the office' });
const rowOf = (page: Page, text: string) => list(page).getByRole('listitem').filter({ hasText: text });
const panel = (page: Page) => page.getByRole('dialog');
const toast = (page: Page, text: string | RegExp) => page.locator('.mantine-Notification-root').filter({ hasText: text });
const card = (page: Page) => panel(page).getByTestId('assign-card');
const writes = (state: WorkQueueState, part: string) => state.saved.filter(s => s.path.includes(part));

async function openQueue(page: Page, path = '/admin/work-queue/') {
  await page.goto(path);
  await expect(page.getByRole('heading', { name: 'Work queue', level: 1 })).toBeVisible();
  await expect(list(page).getByRole('listitem').first()).toBeVisible({ timeout: 30000 });
}
async function open(page: Page, button: RegExp) {
  await list(page).getByRole('button', { name: button }).click();
  await expect(panel(page)).toBeVisible();
}

test.describe('Work queue list', () => {
  test('shows every waiting item with source, type, cleaning type and state tags, as in the design', async ({ page }) => {
    await setupWorkQueue(page);
    await openQueue(page);
    await expect(page.getByRole('status').filter({ hasText: 'items are waiting' })).toHaveText('12 items are waiting, 1 overdue.');
    await expect(list(page).getByRole('listitem')).toHaveCount(12);

    const overdue = rowOf(page, 'Lindqvist household');
    await expect(overdue).toHaveClass(/alert/);
    await expect(overdue.locator('.tags .chip')).toHaveText(['Admin', 'Cleaning', 'Normal', 'Overdue · not started']);
    await expect(overdue.locator('.chip').first()).toHaveClass(/src-admin/);
    await expect(overdue).toContainText('Kallio flat, Lindqvist weekly · 14 Jan 2030 08:00');
    await expect(rowOf(page, 'Eeva Mäkinen').locator('.chip')).toHaveText(['Public booking', 'Cleaning', 'Kotisiivous', 'New booking']);
    await expect(rowOf(page, 'Eeva Mäkinen').locator('.chip').first()).toHaveClass(/src-booking/);
    await expect(rowOf(page, '17–18 Jan 2030').locator('.chip')).toHaveText(['Partner portal', 'Cleaning', 'Normal', 'Needs an employee']);
    await expect(rowOf(page, '17–18 Jan 2030').locator('.chip').first()).toHaveClass(/src-partner/);
    await expect(rowOf(page, '21 Jan 2030 07:00').locator('.chip')).toHaveText(['Partner portal', 'Cleaning', 'Deep clean', 'Awaiting acceptance · offered to Ville Rantanen']);
    await expect(rowOf(page, 'Kamppi Apartments Oy').locator('.chip')).toHaveText(['Partner portal', 'Quote', 'Quote requested']);
    await expect(rowOf(page, 'Key pickup').locator('.chip')).toHaveText(['Partner portal', 'Key handling', 'Needs a staff member']);
    await expect(rowOf(page, 'Key handover')).toContainText('Assigned to Minna Salo · not completed');
    // Completed key requests, accepted partner visits and resolved items are not waiting.
    await expect(list(page)).not.toContainText(/· 5 Jan 2030/);

    // Each kind has its own action.
    await expect(list(page).getByRole('button')).toHaveText(['Set price', 'Reply', 'Reschedule', 'Cancel visit', 'Update notes', 'Assign', 'Assign', 'Offer', 'Assign', 'Assign', 'Offer', 'Complete']);
    // The quote banner repeats nothing here, and Partner operations is no longer in the navigation.
    await expect(page.getByRole('region', { name: 'Pending quote requests' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Partner operations' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Scheduler', exact: true }).last()).toHaveAttribute('href', /\/admin\/shifts\/scheduler/);
  });

  test('Source and Type filter together, with search, sort and an empty-search message, and the URL keeps them', async ({ page }) => {
    await setupWorkQueue(page);
    await openQueue(page);
    await page.getByLabel('Source').selectOption('partner');
    await expect(list(page).getByRole('listitem')).toHaveCount(9);
    await page.getByLabel('Type').selectOption('cleaning');
    await expect(list(page).getByRole('listitem')).toHaveCount(2);
    await page.getByLabel('Source').selectOption('admin');
    await expect(list(page).getByRole('listitem')).toHaveCount(2);
    await expect(list(page)).not.toContainText('Partner portal');
    await page.getByLabel('Source').selectOption('booking');
    await expect(list(page).getByRole('listitem')).toHaveCount(1);
    await page.getByLabel('Source').selectOption('all');
    await page.getByLabel('Type').selectOption('change');
    await expect(list(page).getByRole('listitem')).toHaveCount(4);
    await page.getByLabel('Type').selectOption('key');
    await expect(list(page).getByRole('listitem')).toHaveCount(2);
    await page.getByLabel('Type').selectOption('quote');
    await expect(list(page).getByRole('listitem')).toHaveCount(1);

    await page.getByLabel('Type').selectOption('all');
    await page.getByLabel('Sort').selectOption('desc');
    await expect(list(page).getByRole('listitem').first()).toContainText('Key handover');
    await page.getByLabel('Search').fill('kesko');
    await expect(list(page).getByRole('listitem')).toHaveCount(3);
    await expect(page).toHaveURL(/q=kesko/);
    await expect(page).toHaveURL(/sort=desc/);
    await page.reload();
    await expect(page.getByLabel('Search')).toHaveValue('kesko');
    await expect(page.getByLabel('Sort')).toHaveValue('desc');
    await expect(list(page).getByRole('listitem')).toHaveCount(3);
    await page.getByLabel('Search').fill('nothing like this');
    await expect(page.getByText('Nothing waiting matches “nothing like this”.')).toBeVisible();
    await expect(page.getByText('Showing 0 of 0')).toBeVisible();
  });

  test('pages through a long queue 25 at a time', async ({ page }) => {
    await setupWorkQueue(page, { many: 40 });
    await openQueue(page);
    await expect(page.getByText('Showing 1–25 of 52')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Previous' })).toBeDisabled();
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Showing 26–50 of 52')).toBeVisible();
    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Page 3 of 3')).toBeVisible();
    await expect(list(page).getByRole('listitem')).toHaveCount(2);
    await expect(page.getByRole('button', { name: 'Next' })).toBeDisabled();
    await page.getByRole('button', { name: 'Previous' }).click();
    await expect(page.getByText('Page 2 of 3')).toBeVisible();
  });

  test('an empty queue says so', async ({ page }) => {
    await setupWorkQueue(page, { empty: true });
    await page.goto('/admin/work-queue/');
    await expect(page.getByRole('status').filter({ hasText: 'Nothing is waiting.' })).toBeVisible();
    await expect(page.getByText('New visits, bookings and partner requests appear here when they need the office.')).toBeVisible();
  });

  test('a load failure offers Retry, and a partial failure says what is missing in a toast', async ({ page }) => {
    const state = await setupWorkQueue(page);
    state.queueDown = true; state.partnerDown = true;
    await page.goto('/admin/work-queue/');
    await expect(page.getByRole('alert').filter({ hasText: 'The work queue could not be loaded.' })).toBeVisible();
    state.partnerDown = false;
    await page.getByRole('button', { name: 'Retry' }).click();
    await expect(list(page).getByRole('listitem')).toHaveCount(9);
    await expect(toast(page, 'Unassigned visits and bookings could not be loaded. Partner work is shown.')).toBeVisible();
  });

  test('old Partner operations links open the Work queue', async ({ page }) => {
    await setupWorkQueue(page);
    await page.goto('/admin/partner-requests/?tab=quotes');
    await expect(page).toHaveURL(/\/admin\/work-queue\/\?type=quote/);
    await expect(page.getByLabel('Type')).toHaveValue('quote');
    await expect(list(page).getByRole('listitem')).toHaveCount(1);
    await page.goto('/admin/partner-requests/');
    await expect(page).toHaveURL(/\/admin\/work-queue\/\?source=partner/);
    await expect(page.getByLabel('Source')).toHaveValue('partner');
  });
});

test.describe('Assigning cleaning from the queue', () => {
  test('assigns an admin visit at its own time, with availability, Best fit, toast and the item leaving the queue', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Assign cleaning Kallio flat/);
    await expect(panel(page)).toHaveAccessibleName('Kallio flat, Lindqvist weekly');
    await expect(panel(page).locator('.drawer-head .chip')).toHaveText(['Admin', 'Cleaning', 'Normal']);
    await expect(panel(page).locator('.facts')).toContainText('Keys under the mat.');
    // Only active office employees: no partner users and no one who has left. Free people first; Best fit is the least busy free one.
    const options = card(page).getByLabel('Employee').locator('option');
    await expect(options).toHaveText(['Jari Laine · Free · Best fit · 0 h booked', 'Ville Rantanen · Free · 0 h booked', 'Anna Korhonen · Overlaps 08:00–10:30 · 2.5 h booked', 'Minna Salo · Overlaps 09:00–12:00 · 5 h booked']);
    await expect(card(page).locator('.chip-best')).toHaveText('Best fit');
    await expect(card(page).getByRole('slider', { name: /Time for Jari Laine/ })).toHaveText('8–11');
    await expect(card(page).locator('.scale span')).toHaveText(['00', '06', '12', '18', '24']);

    // Someone already booked: the block turns gold and the warning names the visit.
    await card(page).getByLabel('Employee').selectOption('minna');
    await expect(card(page).locator('.chip-busy')).toHaveText('Overlaps 09:00–12:00');
    await expect(card(page).locator('.b-clash')).toBeVisible();
    await expect(card(page).locator('.b-busy')).toHaveCount(2);
    await expect(card(page).getByRole('status')).toHaveText('Minna Salo already has Aurora clinic 09:00–12:00. Drag the block to a free time, try another day, or go ahead anyway and the Scheduler shows both as an overlap.');
    await expect(card(page).getByRole('button', { name: 'Assign to Minna Salo anyway' })).toBeVisible();

    await card(page).getByLabel('Employee').selectOption('ville');
    await card(page).getByRole('button', { name: 'Assign to Ville Rantanen' }).click();
    await expect(toast(page, 'Kallio flat, Lindqvist weekly is assigned to Ville Rantanen for Mon 14 Jan 08:00–11:00 and now shows in the Scheduler.')).toBeVisible();
    await expect(panel(page)).toHaveCount(0);
    const saved = writes(state, '/visit-assignees');
    expect(saved).toHaveLength(1);
    expect(saved[0].body).toMatchObject({ originalStartUtc: '2030-01-14T06:00:00Z', extraExceptionId: null, mode: 1, employeeIds: ['ville'], rosterVersion: 'roster-1', fromThisDate: false, preview: false });
    await expect(rowOf(page, 'Lindqvist household')).toHaveCount(0);
    await expect(page.getByRole('status').filter({ hasText: 'items are waiting' })).toHaveText('11 items are waiting.');
  });

  test('moves the time with the keyboard and tap, stretches it, changes the day, and saves the new time with the employee', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Assign cleaning Kallio flat/);
    const block = card(page).getByRole('slider', { name: /Time for/ });
    await block.focus();
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowRight');
    await expect(block).toHaveAttribute('aria-valuetext', '08:30–11:30');
    await expect(block).toHaveText('8:30–11:30');
    const end = card(page).getByRole('slider', { name: /End of the visit/ });
    await end.focus();
    await page.keyboard.press('ArrowLeft');
    await expect(end).toHaveAttribute('aria-valuetext', '2 h 45 min');
    // Tapping an empty part of the bar puts the block there, centred and snapped to 15 minutes.
    const bar = card(page).getByTestId('day-bar');
    const box = (await bar.boundingBox())!;
    await bar.click({ position: { x: box.width * (14 / 24), y: 4 } });
    await expect(block).toHaveAttribute('aria-valuetext', '12:45–15:30');
    await card(page).getByRole('button', { name: /Next day for/ }).click();
    await expect(card(page).locator('.pp-day strong')).toHaveText('Tue 15 Jan');
    await expect(card(page).getByRole('button', { name: /Previous day for/ })).toBeEnabled();
    await expect(card(page).getByTestId('moved-note')).toContainText('Originally 14 Jan 2030 08:00, 3 h. The new time, Tue 15 Jan');
    await card(page).getByLabel('Employee').selectOption('anna');
    await card(page).getByRole('button', { name: 'Assign to Anna Korhonen' }).click();
    await expect(toast(page, /is assigned to Anna Korhonen for Tue 15 Jan/)).toBeVisible();
    const saved = writes(state, '/occurrence-exceptions');
    expect(saved).toHaveLength(1);
    const body = saved[0].body;
    expect(body).toMatchObject({ id: null, originalStartUtc: '2030-01-14T06:00:00Z', status: 1, employeeId: 'anna' });
    expect(body.startUtc.slice(0, 10)).toBe('2030-01-15');
    expect((Date.parse(body.endUtc) - Date.parse(body.startUtc)) / 60000).toBe(165);
    expect(writes(state, '/visit-assignees')).toHaveLength(0);
  });

  test('drags the block and its end with the mouse in 15-minute steps', async ({ page, isMobile }) => {
    test.skip(isMobile, 'Touch devices move the block by tapping the bar or with the keys, covered above.');
    await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Assign cleaning Kallio flat/);
    const bar = card(page).getByTestId('day-bar');
    const box = (await bar.boundingBox())!;
    const block = card(page).getByRole('slider', { name: /Time for/ });
    const from = (await block.boundingBox())!;
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(from.x + from.width / 2 + box.width * (4.1 / 24), from.y + from.height / 2, { steps: 6 });
    await page.mouse.up();
    await expect(block).toHaveAttribute('aria-valuetext', '12:00–15:00');
    const end = (await card(page).getByRole('slider', { name: /End of the visit/ }).boundingBox())!;
    await page.mouse.move(end.x + end.width / 2, end.y + end.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width * (16.55 / 24), end.y + end.height / 2, { steps: 6 });
    await page.mouse.up();
    await expect(block).toHaveAttribute('aria-valuetext', '12:00–16:30');
  });

  test('a branch visit is offered with its revision, after moving it when the time changed', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Assign cleaning Helsinki showroom/);
    await expect(panel(page).locator('.drawer-head .chip')).toHaveText(['Admin', 'Cleaning', 'Deep clean']);
    await card(page).getByRole('slider', { name: /Time for/ }).focus();
    await page.keyboard.press('ArrowLeft');
    await card(page).getByLabel('Employee').selectOption('minna');
    await card(page).getByRole('button', { name: 'Assign to Minna Salo' }).click();
    await expect(toast(page, 'is assigned to Minna Salo for Sun 20 Jan 08:45–11:45')).toBeVisible();
    expect(writes(state, '/occurrence-exceptions')[0].body).toMatchObject({ id: 'extra-6', originalStartUtc: null, status: 4, employeeId: null });
    expect(writes(state, '/branch-visits/shifts/shift-virtanen/visits/extra-6/employee')[0].body).toEqual({ employeeId: 'minna', offerRevision: 0 });
  });

  test('a public booking keeps its time: confirm it, then assign it', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Assign cleaning B-1042/);
    await expect(panel(page)).toHaveAccessibleName('Booking B-1042');
    await expect(panel(page).locator('.facts')).toContainText('040 123 4567 · eeva@example.fi');
    await expect(panel(page).locator('.facts')).toContainText('Kotisiivous, 180 min');
    await expect(card(page).getByText('A public booking keeps the day and time the customer booked.')).toBeVisible();
    await expect(card(page).getByRole('slider', { name: /End of the visit/ })).toHaveCount(0);
    await expect(card(page).getByRole('button', { name: /Next day/ })).toBeDisabled();
    // Jari has another booking 08:00–10:00 that day.
    await card(page).getByLabel('Employee').selectOption('jari');
    await expect(card(page).locator('.chip-busy')).toHaveText('Overlaps 08:00–10:00');
    await panel(page).getByRole('button', { name: 'Confirm booking' }).click();
    await expect(toast(page, 'Booking B-1042 is confirmed. Assign an employee next.')).toBeVisible();
    expect(writes(state, '/bookings/bk-1042/confirm')).toHaveLength(1);
    await expect(rowOf(page, 'Eeva Mäkinen')).toContainText('Unassigned');
    await open(page, /Assign cleaning B-1042/);
    await expect(panel(page).getByRole('button', { name: 'Confirm booking' })).toHaveCount(0);
    await card(page).getByRole('button', { name: /^Assign to / }).click();
    await expect(toast(page, /Booking B-1042 is assigned to .* for Wed 16 Jan 08:00–11:00/)).toBeVisible();
    expect(writes(state, '/bookings/bk-1042/assign')).toHaveLength(1);
    await expect(rowOf(page, 'Eeva Mäkinen')).toHaveCount(0);
  });

  test('a booking can be cancelled after a confirmation step', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Assign cleaning B-1042/);
    await panel(page).getByRole('button', { name: 'Cancel booking' }).click();
    const ask = panel(page).getByRole('alertdialog', { name: 'Cancel booking' });
    await ask.getByRole('button', { name: 'Keep' }).click();
    await expect(ask).toHaveCount(0);
    await panel(page).getByRole('button', { name: 'Cancel booking' }).click();
    await ask.getByRole('button', { name: 'Cancel booking' }).click();
    await expect(toast(page, 'Booking B-1042 is cancelled.')).toBeVisible();
    expect(writes(state, '/bookings/bk-1042/cancel')).toHaveLength(1);
  });

  test('an admin visit can be cancelled with an optional reason', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Assign cleaning Kallio flat/);
    await panel(page).getByRole('button', { name: 'Cancel visit' }).click();
    await panel(page).getByRole('alertdialog').getByLabel('Reason (optional)').fill('Customer away');
    await panel(page).getByRole('alertdialog').getByRole('button', { name: 'Cancel visit' }).click();
    await expect(toast(page, 'Kallio flat, Lindqvist weekly on 14 Jan 2030 08:00 is cancelled.')).toBeVisible();
    expect(writes(state, '/occurrence-exceptions')[0].body).toMatchObject({ status: 3, reason: 'Customer away', originalStartUtc: '2030-01-14T06:00:00Z' });
  });

  test('a server refusal shows an error toast and keeps the panel open', async ({ page }) => {
    const state = await setupWorkQueue(page);
    state.fail.add('/visit-assignees');
    await openQueue(page);
    await open(page, /Assign cleaning Kallio flat/);
    await card(page).getByRole('button', { name: /^Assign to / }).click();
    await expect(toast(page, 'Refused by the server: /visit-assignees.')).toBeVisible();
    await expect(panel(page)).toBeVisible();
    await expect(rowOf(page, 'Lindqvist household')).toHaveCount(1);
  });

  test('the panel closes with Escape and the close button, returning focus, and help hides behind the i', async ({ page }) => {
    await setupWorkQueue(page);
    await openQueue(page);
    const button = list(page).getByRole('button', { name: /Assign cleaning Kallio flat/ });
    await button.click();
    await expect(panel(page).getByRole('button', { name: 'Close' })).toBeFocused();
    const tip = panel(page).getByRole('tooltip').first();
    await expect(tip).toBeHidden();
    await panel(page).getByRole('button', { name: 'More information' }).first().focus();
    await expect(tip).toBeVisible();
    await expect(tip).toContainText('Pick the employee in the card');
    await page.keyboard.press('Escape');
    await expect(panel(page)).toHaveCount(0);
    await expect(button).toBeFocused();
    await button.click();
    await panel(page).getByRole('button', { name: 'Close' }).click();
    await expect(panel(page)).toHaveCount(0);
  });

  test('a booking link opens its panel', async ({ page }) => {
    await setupWorkQueue(page);
    await page.goto('/admin/work-queue/?booking=bk-1042');
    await expect(panel(page)).toHaveAccessibleName('Booking B-1042');
  });

  test('a link to a booking that already has an employee opens it on the Bookings page', async ({ page }) => {
    await setupWorkQueue(page);
    await page.goto('/admin/work-queue/?booking=bk-old');
    await expect(page).toHaveURL(/\/admin\/bookings\/\?booking=bk-old$/);
    await expect(page.getByRole('dialog', { name: 'Booking B-0999' })).toBeVisible();
  });
});

test.describe('Partner work in the queue', () => {
  test('offers a partner request inside its window; automatic acceptance is said and shown', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Offer cleaning Nordea office/);
    await expect(panel(page).locator('.facts')).toContainText('Any time in the window');
    await expect(panel(page).locator('.facts')).toContainText('€180.00 excl. VAT (25.5%)');
    await expect(panel(page).getByText('Offer to', { exact: true })).toBeVisible();
    await expect(card(page).getByText('The length comes from the property’s estimate.')).toBeVisible();
    await expect(card(page).getByRole('slider', { name: /End of the visit/ })).toHaveCount(0);
    await expect(card(page).locator('.pp-day strong')).toHaveText('Thu 17 Jan');
    await card(page).getByRole('button', { name: /Next day for/ }).click();
    await expect(card(page).locator('.pp-day strong')).toHaveText('Fri 18 Jan');
    await expect(card(page).getByRole('button', { name: /Next day for/ })).toBeDisabled();
    await card(page).getByLabel('Employee').selectOption('anna');
    await expect(card(page).locator('.pp-sub')).toContainText('Accepts automatically');
    await expect(card(page).getByTestId('moved-note')).toContainText('The partner asked for 17–18 Jan 2030, 3 h.');
    await card(page).getByRole('button', { name: 'Send offer to Anna Korhonen' }).click();
    await expect(toast(page, 'Anna Korhonen accepts work automatically, so Nordea office (Fri 18 Jan 09:00–12:00) is accepted and now shows in the Scheduler.')).toBeVisible();
    expect(writes(state, '/partner/requests/p311/offer')[0].body).toEqual({ version: 3, employeeId: 'anna', plannedStartLocal: '2030-01-18T09:00' });
  });

  test('re-offers a request waiting for acceptance; a fixed start is kept', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Offer cleaning Kesko store/);
    await expect(card(page).getByLabel('Employee')).toHaveValue('ville');
    await expect(card(page).getByText(/The partner asked for this exact start/)).toBeVisible();
    await card(page).getByLabel('Employee').selectOption('jari');
    await card(page).getByRole('button', { name: 'Send offer to Jari Laine' }).click();
    await expect(toast(page, 'Offer for Kesko store, Mon 21 Jan 07:00–09:00, sent to Jari Laine. It moves to the Scheduler when they accept.')).toBeVisible();
    expect(writes(state, '/partner/requests/p314/offer')[0].body).toEqual({ version: 3, employeeId: 'jari', plannedStartLocal: null });
    await expect(rowOf(page, '21 Jan 2030 07:00')).toContainText('Awaiting acceptance · offered to Jari Laine');
  });

  test('edits and cancels a partner request; a reason is required for the partner', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Offer cleaning Nordea office/);
    await panel(page).getByRole('button', { name: 'Edit or reschedule' }).click();
    await panel(page).getByLabel('To', { exact: true }).fill('2030-01-16');
    await panel(page).getByRole('button', { name: 'Save changes' }).click();
    await expect(panel(page).getByRole('alert')).toHaveText('The end date must be on or after the start date.');
    await panel(page).getByLabel('To', { exact: true }).fill('2030-01-19');
    await panel(page).getByLabel('Cleaning instructions').fill('Ring the bell.');
    await panel(page).getByRole('button', { name: 'Save changes' }).click();
    await expect(toast(page, 'Nordea office is updated. Send a new employee offer.')).toBeVisible();
    expect(writes(state, '/partner/requests/p311')[0].body).toMatchObject({ fromDate: '2030-01-17', toDate: '2030-01-19', exactStartLocalTime: null, notes: 'Ring the bell.', version: 3 });
    await expect(rowOf(page, '17–19 Jan 2030')).toBeVisible();

    await open(page, /Offer cleaning Nordea office/);
    await panel(page).getByRole('button', { name: 'Cancel request' }).click();
    const ask = panel(page).getByRole('alertdialog', { name: 'Cancel request' });
    await ask.getByRole('button', { name: 'Cancel request' }).click();
    await expect(panel(page).getByRole('alert')).toHaveText('Enter a reason for the partner.');
    await ask.getByLabel('Reason, shown to the partner').fill('No staff that week.');
    await ask.getByRole('button', { name: 'Cancel request' }).click();
    await expect(toast(page, 'Nordea office is cancelled. The partner sees your reason.')).toBeVisible();
    expect(writes(state, '/partner/requests/p311/cancel')[0].body).toEqual({ version: 4, reason: 'No staff that week.' });
  });

  test('prices a quote, or rejects it with a reason', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Set price quote Kamppi studio 12/);
    await expect(panel(page)).toHaveAccessibleName('Price Kamppi studio 12');
    await expect(panel(page).locator('.facts')).toContainText('Rooms2');
    await panel(page).getByRole('button', { name: 'Save price' }).click();
    await expect(panel(page).getByRole('alert')).toHaveText('Enter the price and the estimated duration.');
    await panel(page).getByLabel('Price excl. VAT (€)').fill('95');
    await panel(page).getByLabel('Estimated duration (minutes)').fill('150');
    await panel(page).getByRole('button', { name: 'Save price' }).click();
    await expect(toast(page, 'Kamppi studio 12 is priced at €95.00 excl. VAT. The partner can now book cleaning there.')).toBeVisible();
    expect(writes(state, '/partner/branches/b-studio/pricing')[0].body).toEqual({ priceNet: 95, vatRatePercent: 25.5, estimatedDurationMinutes: 150 });
    await expect(rowOf(page, 'Kamppi Apartments Oy')).toHaveCount(0);
  });

  test('rejects a quote with a reason the partner sees', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Set price quote/);
    await panel(page).getByRole('button', { name: 'Reject quote' }).click();
    await panel(page).getByRole('alertdialog').getByLabel('Reason, shown to the partner').fill('Outside our area.');
    await panel(page).getByRole('alertdialog').getByRole('button', { name: 'Reject quote' }).click();
    await expect(toast(page, 'The quote for Kamppi studio 12 is rejected and the partner sees your reason.')).toBeVisible();
    expect(writes(state, '/quote-rejection')[0].body).toEqual({ reason: 'Outside our area.' });
  });

  test('reschedules a visit as asked: applies the change, then offers it at the chosen time', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Reschedule change request/);
    await expect(panel(page).locator('.message')).toHaveText('The office is closed on Friday. Could you come next week instead?');
    await expect(panel(page).getByText('Asks to move the visit to 22–23 Jan 2030.')).toBeVisible();
    await expect(panel(page).locator('.facts')).toContainText('18 Jan 2030 09:00 with Ville Rantanen');
    await expect(panel(page).getByText('Move the visit', { exact: true })).toBeVisible();
    await expect(card(page).locator('.pp-day strong')).toHaveText('Tue 22 Jan');
    // The visit's current assignee is chosen first.
    await expect(card(page).getByLabel('Employee')).toHaveValue('ville');
    await expect(card(page).locator('.chip-busy')).toHaveText('Overlaps 10:00–13:00');
    await card(page).getByRole('slider', { name: /Time for/ }).focus();
    for (let i = 0; i < 16; i++) await page.keyboard.press('ArrowRight');
    await expect(card(page).locator('.chip-free')).toHaveText('Free');
    await panel(page).getByLabel(/Reply to the partner/).fill('See you on Tuesday afternoon.');
    await card(page).getByRole('button', { name: 'Move with Ville Rantanen' }).click();
    await expect(toast(page, 'Nordea office is moved to Tue 22 Jan 13:00–16:00 with Ville Rantanen. The partner gets your reply.')).toBeVisible();
    expect(writes(state, '/change-requests/ch-move/apply')[0].body).toEqual({ version: 5, resolution: 'See you on Tuesday afternoon.' });
    expect(writes(state, '/partner/requests/p309/offer')[0].body).toEqual({ version: 6, employeeId: 'ville', plannedStartLocal: '2030-01-22T13:00' });
  });

  test('cancels a visit as asked, updates instructions, answers a message, and needs a reply to decline', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Cancel visit change request/);
    await expect(panel(page).getByText('Asks to cancel the visit.')).toBeVisible();
    await panel(page).getByRole('button', { name: 'Decline the request' }).click();
    await expect(panel(page).getByRole('alert')).toHaveText('Write a reply so the partner knows why.');
    await panel(page).getByRole('button', { name: 'Cancel the visit' }).click();
    await expect(toast(page, 'The visit at Aurora clinic is cancelled and the partner is told.')).toBeVisible();
    expect(writes(state, '/change-requests/ch-cancel/apply')[0].body).toEqual({ version: 2, resolution: 'Cancelled as you asked.' });

    await open(page, /Update notes change request/);
    await expect(panel(page).getByLabel('Cleaning instructions, shown to the assignee')).toHaveValue('Side entrance, code 1234.');
    await panel(page).getByLabel('Cleaning instructions, shown to the assignee').fill('Side entrance, code 9999.');
    await panel(page).getByRole('button', { name: 'Save instructions and resolve' }).click();
    await expect(toast(page, 'The instructions for Nordea office are updated and the request is resolved.')).toBeVisible();
    expect(writes(state, '/partner/requests/p311')[0].body).toMatchObject({ notes: 'Side entrance, code 9999.', version: 3 });
    expect(writes(state, '/change-requests/ch-notes/resolve')[0].body).toEqual({ resolution: 'Instructions updated.' });

    await open(page, /Reply change request/);
    await expect(panel(page).getByRole('button', { name: 'Decline the request' })).toHaveCount(0);
    await panel(page).getByRole('button', { name: 'Send reply and resolve' }).click();
    await expect(panel(page).getByRole('alert')).toHaveText('Write a reply to the partner.');
    await panel(page).getByLabel('Reply to the partner').fill('Yes, we will.');
    await panel(page).getByRole('button', { name: 'Send reply and resolve' }).click();
    await expect(toast(page, 'The message from Kesko store, Kamppi is answered and resolved.')).toBeVisible();
    expect(writes(state, '/change-requests/ch-msg/resolve')[0].body).toEqual({ resolution: 'Yes, we will.' });
  });

  test('declines a change request with a reply', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Reschedule change request/);
    await panel(page).getByLabel(/Reply to the partner/).fill('We are fully booked that week.');
    await panel(page).getByRole('button', { name: 'Decline the request' }).click();
    await expect(toast(page, 'The request from Nordea branch, Mannerheimintie is declined and the partner sees your reply.')).toBeVisible();
    expect(writes(state, '/change-requests/ch-move/resolve')[0].body).toEqual({ resolution: 'We are fully booked that week.' });
    expect(writes(state, '/apply')).toHaveLength(0);
  });

  test('assigns a key request, then adds a photo and completes another', async ({ page }) => {
    const state = await setupWorkQueue(page);
    await openQueue(page);
    await open(page, /Assign key handling Kesko store/);
    await expect(panel(page).getByText('Staff member who collects or hands over the keys')).toBeVisible();
    await expect(card(page).getByRole('slider')).toHaveCount(0);
    await card(page).getByLabel('Employee').selectOption('minna');
    await expect(card(page).locator('.chip-free')).toHaveText('Free all day');
    await card(page).getByRole('button', { name: 'Assign to Minna Salo' }).click();
    await expect(toast(page, 'Minna Salo will handle the key pickup on 19 Jan 2030. Mark it completed when the keys have changed hands.')).toBeVisible();
    expect(writes(state, '/key-requests/k-kesko/assignment')[0].body).toEqual({ revision: 1, employeeId: 'minna' });

    await open(page, /Complete key handling Aurora clinic/);
    await expect(panel(page).getByText('Minna Salo is handling it.', { exact: false })).toBeVisible();
    await panel(page).getByLabel('Photo of the keys').setInputFiles({ name: 'keys.png', mimeType: 'image/png', buffer: Buffer.from('89504e470d0a1a0a', 'hex') });
    await expect(panel(page).getByRole('button', { name: 'Add a photo (1 added)' })).toBeVisible();
    await panel(page).getByRole('button', { name: 'Mark completed' }).click();
    await expect(toast(page, 'The key handover for Aurora clinic is completed.')).toBeVisible();
    expect(writes(state, '/key-requests/k-aurora/complete')[0].body).toEqual({ revision: 1 });
    expect(writes(state, '/key-requests/k-aurora/photos')).toHaveLength(1);
  });
});

test('the partner key request fee is set on Services and confirmed in a toast', async ({ page }) => {
  const state = await setupWorkQueue(page);
  await page.route(/\/api\/v1\/admin\/services(?:\?.*)?$/, route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*' }, json: [] }));
  await page.goto('/admin/services/');
  const form = page.getByRole('form', { name: 'Key request fee' });
  const fee = form.getByLabel('Partner key request fee, excl. VAT (€)');
  await expect(fee).toHaveValue('25.00');
  await fee.fill('30');
  await form.getByRole('button', { name: 'Save fee' }).click();
  await expect(toast(page, 'Key request fee saved: €30.00 excl. VAT. New key requests use this fee.')).toBeVisible();
  expect(state.saved.find(s => s.path === '/partner/key-request-fee')?.body).toEqual({ feeNet: 30 });
});

test('a key request fee that cannot be loaded says so in its box, with no toast, and Retry loads it', async ({ page }) => {
  const state = await setupWorkQueue(page);
  state.partnerDown = true;
  await page.route(/\/api\/v1\/admin\/services(?:\?.*)?$/, route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*' }, json: [] }));
  await page.goto('/admin/services/');
  const form = page.getByRole('form', { name: 'Key request fee' });
  await expect(form.getByRole('alert')).toHaveText('The key request fee could not be loaded.');
  await expect(form.getByLabel('Partner key request fee, excl. VAT (€)')).toBeDisabled();
  await expect(page.locator('.mantine-Notification-root')).toHaveCount(0);
  state.partnerDown = false;
  await form.getByRole('button', { name: 'Retry' }).click();
  await expect(form.getByLabel('Partner key request fee, excl. VAT (€)')).toHaveValue('25.00');
  await expect(form.getByRole('alert')).toHaveCount(0);
});
