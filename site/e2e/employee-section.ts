import { expect, type Page } from '@playwright/test';

// Employee sections: a tab row on larger screens, and a menu button on phones.
export async function chooseEmployeeSection(page: Page, name: string) {
  const tab = page.getByRole('tab', { name, exact: true });
  if (await tab.isVisible()) { await tab.click(); return; }
  await page.locator('#employee-section-menu-button').click();
  await page.getByRole('navigation', { name: 'My account sections' }).getByRole('button', { name, exact: true }).click();
  await expect(page.locator('#employee-section-menu-button')).toHaveAttribute('aria-expanded', 'false');
}
