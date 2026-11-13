import { expect, type Page } from '@playwright/test';

// Employee sections: a tab row on larger screens, and a menu button on phones.
export async function chooseEmployeeSection(page: Page, name: string) {
  const tab = page.getByRole('tab', { name, exact: true });
  const menu = page.locator('#employee-section-menu-button');
  // Wait for whichever control this screen shows; isVisible() alone does not wait, so a tab row that has not rendered yet looked like a missing one.
  await expect.poll(async () => (await tab.isVisible()) || (await menu.isVisible()), { timeout: 20_000 }).toBe(true);
  if (await tab.isVisible()) { await tab.click(); return; }
  await page.locator('#employee-section-menu-button').click();
  await page.getByRole('navigation', { name: 'My account sections' }).getByRole('button', { name, exact: true }).click();
  await expect(page.locator('#employee-section-menu-button')).toHaveAttribute('aria-expanded', 'false');
}
