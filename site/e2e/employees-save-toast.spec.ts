import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const EMAIL_TAKEN = 'Another employee already uses this email. Use a different email for this employee, or open that employee to change theirs.';

test('a rejected employee edit explains the duplicate email as a toast and in the dialog, and keeps the edited values', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = { id: 'emp-a', email: 'alice@example.invalid', firstName: 'Alice', lastName: 'Cleaner', role: 'employee', isActive: true, phone: '', defaultHours: {}, skills: [], serviceAreas: [] };
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, r => r.fulfill({ headers, json: [employee] }));
  await page.route(/\/api\/v1\/admin\/employees\/emp-a$/, r => {
    if (r.request().method() === 'OPTIONS') return r.fulfill({ headers, status: 200 });
    if (r.request().method() === 'PUT') return r.fulfill({ headers, status: 409, json: { title: 'Employee identity conflict', detail: EMAIL_TAKEN } });
    return r.fulfill({ headers, json: employee });
  });
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
  await page.getByLabel('First name').fill('Alicia');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText('Employee not updated').first()).toBeVisible();
  await expect(page.getByText(EMAIL_TAKEN).first()).toBeVisible();
  await expect(page.getByLabel('First name')).toHaveValue('Alicia');
});
