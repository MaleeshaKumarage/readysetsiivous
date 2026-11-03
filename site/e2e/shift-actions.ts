import { expect, type Locator, type Page } from '@playwright/test';

/** Opens the More actions menu for the named shift row and clicks one item. */
export async function shiftAction(page: Page, shiftName: string, label: string) {
  await page.getByRole('button', { name: `More actions for ${shiftName}`, exact: true }).click();
  await page.getByRole('menuitem', { name: label, exact: true }).click();
}

/** Opens the More actions menu for the shift row at index (0-based, data rows only) and clicks one item. */
export async function shiftActionAt(page: Page, label: string, index = 0) {
  await page.getByRole('button', { name: /^More actions for / }).nth(index).click();
  await page.getByRole('menuitem', { name: label, exact: true }).click();
}

/** Opens the More actions menu inside a known shift row locator and clicks one item. */
export async function shiftRowAction(page: Page, row: Locator, label: string) {
  await row.getByRole('button', { name: /^More actions for / }).click();
  await page.getByRole('menuitem', { name: label, exact: true }).click();
}

/** Opens the shift workspace for the named shift row. */
export async function openShiftWorkspace(page: Page, shiftName: string) {
  await page.getByRole('row').filter({ hasText: shiftName }).getByRole('button', { name: 'Manage', exact: true }).click();
  await expect(page.getByRole('dialog', { name: `Manage — ${shiftName}`, exact: true })).toBeVisible();
}
