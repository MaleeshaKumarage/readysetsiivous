import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Dashboard charts: visits and hours per day of a chosen month, sortable customers, and each cleaner's weekly donut split by customer.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

// A day list for the requested month: Alpha Oy has visits on the 3rd and 9th, Beta Ltd on the 9th.
function dayList(month: string) {
  const [y, m] = month.split('-').map(Number);
  const count = new Date(y, m, 0).getDate();
  return Array.from({ length: count }, (_, index) => {
    const date = `${month}-${String(index + 1).padStart(2, '0')}`;
    const companies = [];
    if (index + 1 === 3) companies.push({ companyId: 'c1', customerName: 'Alpha Oy', scheduledVisits: 5, scheduledHours: 10 });
    if (index + 1 === 9) {
      companies.push({ companyId: 'c1', customerName: 'Alpha Oy', scheduledVisits: 1, scheduledHours: 2 });
      companies.push({ companyId: 'c2', customerName: 'Beta Ltd', scheduledVisits: 2, scheduledHours: 4 });
    }
    const visits = companies.reduce((sum, c) => sum + c.scheduledVisits, 0);
    const scheduledHours = companies.reduce((sum, c) => sum + c.scheduledHours, 0);
    return { date, scheduledVisits: visits, scheduledHours, actualHours: index + 1 === 3 ? 9 : 0, companies };
  });
}

async function setup(page: Page) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/work-queue/, route => route.fulfill({ headers, json: { view: 'needs-action', source: 'all', items: [], total: 0, counts: { 'needs-action': 3, scheduled: 5, 'in-progress': 0, completed: 0, cancelled: 0 } } }));
  await page.route(/\/api\/v1\/me\/inbox$/, route => route.fulfill({ headers, json: { unread: 0, items: [] } }));
  await page.route(/\/api\/v1\/admin\/dashboard\/daily/, route => {
    const month = new URL(route.request().url()).searchParams.get('month') ?? '2026-11';
    return route.fulfill({ headers, json: { month, days: dayList(month) } });
  });
}

test('the visits and hours charts plot each day of the month, and customers follow the chosen order', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/');
  const visits = page.getByRole('img', { name: /^Visits per day/ });
  await expect(visits).toBeVisible();
  await expect(visits).toHaveAttribute('aria-label', /Alpha Oy: 6 in total/);
  const hours = page.getByRole('img', { name: /^Scheduled and actual hours per day/ });
  await expect(hours).toBeVisible();
  await expect(hours).toHaveAttribute('aria-label', /Scheduled hours in total: 16\.0\. Actual hours in total: 9\.0/);
  // Sorting by name puts Alpha before Beta in the legend.
  await page.getByRole('textbox', { name: 'Sort customers by' }).click();
  await page.getByRole('option', { name: 'Customer name', exact: true }).click();
  const legend = await visits.locator('xpath=ancestor::div[1]').locator('p').allInnerTexts().then(all => all.filter(text => text === 'Alpha Oy' || text === 'Beta Ltd'));
  expect(legend).toEqual(['Alpha Oy', 'Beta Ltd']);
});

test('the month buttons move the charts to another month', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/');
  const visits = page.getByRole('img', { name: /^Visits per day/ });
  await expect(visits).toBeVisible();
  await page.getByRole('button', { name: 'Previous month' }).first().click();
  await expect(page.getByRole('img', { name: /^Visits per day/ })).toBeVisible();
  await expect(page.getByText(/Visits per day|No visits in/).first()).toBeVisible();
});

test('each cleaner has a donut split by customer against the 37.5 hour full week, in a scrollable list', async ({ page }) => {
  await setup(page);
  const people = Array.from({ length: 9 }, (_, index) => ({ employeeId: `e${index}`, name: `Cleaner ${index + 1}`, scheduledHours: index === 1 ? 40 : 30 - index,
    companies: [{ companyId: 'c1', customerName: 'Alpha Oy', scheduledHours: 20 }, { companyId: 'c2', customerName: 'Beta Ltd', scheduledHours: index === 1 ? 20 : 10 - index }] }));
  await page.route(/\/api\/v1\/admin\/dashboard\/employee-hours/, route => route.fulfill({ status: 200, headers, json: { weekStart: '2026-11-09', fullTimeHours: 37.5, employees: people } }));
  await page.goto('/admin/');
  await expect(page.getByRole('img', { name: /^Cleaner 1: 30\.0 of 37\.5 scheduled hours, Alpha Oy 20\.0, Beta Ltd 10\.0/ })).toBeVisible();
  await expect(page.getByRole('img', { name: /^Cleaner 2: 40\.0 of 37\.5 scheduled hours/ })).toBeVisible();
  await expect(page.getByText('40.0 of 37.5 h · over')).toBeVisible();
  const list = page.getByRole('region', { name: "Cleaners' weekly hours" });
  await expect(list).toBeVisible();
  // One row: the donuts sit side by side and the row scrolls sideways when there are more cleaners than fit.
  const scrollable = await list.evaluate(el => getComputedStyle(el).overflowX === 'auto' && el.scrollWidth > el.clientWidth && el.scrollHeight <= el.clientHeight + 1);
  expect(scrollable).toBe(true);
});

test('a KPI tile opens the work queue on the view it names', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/');
  await page.getByRole('link', { name: /Scheduled visits/ }).click();
  await expect(page).toHaveURL(/work-queue\/\?view=scheduled/);
  await expect(page.getByRole('tab', { name: /^Scheduled/ })).toHaveAttribute('aria-selected', 'true');
});
