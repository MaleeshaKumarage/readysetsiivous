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

/** Opens the shift workspace for the named shift row from its More actions menu. */
export async function openShiftWorkspace(page: Page, shiftName: string) {
  await page.getByRole('row').filter({ hasText: shiftName }).getByRole('button', { name: /^More actions for / }).click();
  await page.getByRole('menuitem', { name: 'View details', exact: true }).click();
  await expect(page.getByRole('dialog', { name: `Manage — ${shiftName}`, exact: true })).toBeVisible();
}

/** Confirms the Deactivate, Reactivate or Delete popup opened from a shift's More actions menu. */
export async function confirmShiftPopup(page: Page, label: 'Deactivate' | 'Reactivate' | 'Delete') {
  const popup = page.getByRole('dialog', { name: new RegExp(`^${label} “`) });
  await expect(popup).toBeVisible();
  await popup.getByRole('button', { name: label, exact: true }).click();
}
