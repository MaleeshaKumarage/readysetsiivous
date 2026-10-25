import { test, expect, type Locator, type Page } from '@playwright/test';

async function keyboardFocus(page: Page, target: Locator) {
  await target.focus();
  await page.keyboard.press('Tab');
  await page.keyboard.press('Shift+Tab');
  await expect(target).toBeFocused();
  await expect.poll(() => target.evaluate(element => getComputedStyle(element).boxShadow)).not.toBe('none');
}

for (const [lang, open, close, dismiss] of [
  ['en', 'Open menu', 'Close menu', 'Dismiss tooltip'],
  ['fi', 'Avaa valikko', 'Sulje valikko', 'Sulje vihje'],
  ['sv', 'Öppna meny', 'Stäng meny', 'Stäng tips'],
]) {
  test(`${lang}: keyboard users can open and close navigation`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/${lang}/`);
    const toggle = page.getByRole('button', { name: open, exact: true });
    await keyboardFocus(page, toggle);
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: close, exact: true })).toHaveAttribute('aria-expanded', 'true');
    await expect(page.locator('#mobile-menu')).toBeVisible();
    await page.keyboard.press('Space');
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await expect(page.locator('#mobile-menu')).not.toBeVisible();
  });

  test(`${lang}: WhatsApp hint can be dismissed with the keyboard`, async ({ page }) => {
    await page.goto(`/${lang}/`);
    const button = page.getByRole('button', { name: dismiss, exact: true });
    await keyboardFocus(page, button);
    await page.keyboard.press('Enter');
    await expect(button).not.toBeVisible();
    const chat = page.locator('a.whatsapp-pulse');
    await expect(chat).toHaveAttribute('href', 'https://wa.me/358468044231');
    await keyboardFocus(page, chat);
  });
}

test('all FAQ answers and quote links remain keyboard accessible', async ({ page }) => {
  await page.goto('/en/');
  const questions = page.locator('#faq button');
  await expect(questions).toHaveCount(6);
  for (let index = 0; index < 6; index++) {
    const question = questions.nth(index);
    await keyboardFocus(page, question);
    await page.keyboard.press('Enter');
    await expect(question).toHaveAttribute('aria-expanded', 'true');
    await expect(page.locator(`#faq-panel-${index}`)).toBeVisible();
    await page.keyboard.press('Space');
    await expect(question).toHaveAttribute('aria-expanded', 'false');
    await expect(page.locator(`#faq-panel-${index}`)).not.toBeVisible();
  }
  // Opening another answer keeps the accordion limited to one expanded item.
  await questions.nth(0).click();
  await expect(page.locator('#faq-panel-0')).toBeVisible();
  await questions.nth(1).click();
  await expect(questions.nth(0)).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('#faq-panel-0')).not.toBeVisible();
  await expect(questions.nth(1)).toHaveAttribute('aria-expanded', 'true');
  await expect(page.locator('#faq-panel-1')).toBeVisible();
  await expect(page.locator('#faq button[aria-expanded="true"]')).toHaveCount(1);
  await questions.nth(1).click();
  await expect(page.locator('#faq-panel-1')).not.toBeVisible();
  const emergency = page.locator('a[href*="wa.me"][href*="text="]').first();
  await keyboardFocus(page, emergency);
  await expect(emergency).toHaveAttribute('target', '_blank');
});

test('quote errors describe their fields and a valid submission exposes busy state', async ({ page }) => {
  await page.addInitScript(() => {
    (window as any).__quoteUrls = [];
    window.open = (url) => { (window as any).__quoteUrls.push(String(url)); return null; };
  });
  await page.goto('/en/');
  const submit = page.locator('#quote button[type="submit"]');
  await keyboardFocus(page, submit);
  await page.keyboard.press('Enter');
  await expect(page.locator('#service')).toBeFocused();
  for (const id of ['service', 'size', 'city', 'date']) {
    await expect(page.locator(`#${id}`)).toHaveAttribute('aria-required', 'true');
    await expect(page.locator(`#${id}`)).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator(`#${id}`)).toHaveAttribute('aria-describedby', `${id}-error`);
    await expect(page.locator(`#${id}-error`)).toBeVisible();
  }
  await page.locator('#service').selectOption('homeCleaning');
  await page.locator('#size').selectOption('sizeSmall');
  await page.locator('#city').fill('Helsinki');
  await page.locator('#date').fill('2099-01-15');
  for (const id of ['service', 'size', 'city', 'date']) {
    await expect(page.locator(`#${id}`)).toHaveAttribute('aria-invalid', 'false');
    await expect(page.locator(`#${id}`)).not.toHaveAttribute('aria-describedby');
  }
  await submit.click();
  await expect(submit).toHaveAttribute('aria-busy', 'true');
  await expect(submit).toBeDisabled();
  await expect.poll(() => page.evaluate(() => (window as any).__quoteUrls.length)).toBe(1);
  const url = await page.evaluate(() => (window as any).__quoteUrls[0]);
  expect(decodeURIComponent(url)).toContain('Helsinki');
  await expect(submit).toHaveAttribute('aria-busy', 'false');
  await expect(submit).toBeEnabled();
});
