import { expect, type Page, type TestInfo } from '@playwright/test';

// Checks how a design-system grid is laid out, not only that its cells exist:
// rows must not overlap each other, cell text must not be clipped vertically or horizontally,
// and a screenshot is kept for review.
export async function expectGridLaidOut(page: Page, testInfo: TestInfo, name: string) {
  const rows = page.getByRole('row').filter({ has: page.getByRole('cell') });
  const count = await rows.count();
  expect(count, `${name} has rows`).toBeGreaterThan(0);

  const boxes: { top: number; bottom: number }[] = [];
  for (let index = 0; index < count; index += 1) {
    const box = await rows.nth(index).boundingBox();
    if (box) boxes.push({ top: box.y, bottom: box.y + box.height });
  }
  for (let index = 1; index < boxes.length; index += 1) {
    expect(boxes[index].top, `${name} row ${index} does not overlap the row above`).toBeGreaterThanOrEqual(boxes[index - 1].bottom - 1);
  }

  const clipped = await page.getByRole('cell').evaluateAll(cells =>
    cells.filter(cell => {
      const content = cell.querySelector('*') ?? cell;
      if (content.scrollHeight > cell.clientHeight + 2 || content.scrollWidth > cell.clientWidth + 2) return true;
      const box = cell.getBoundingClientRect();
      return Array.from(cell.querySelectorAll('*')).some(el => {
        const b = el.getBoundingClientRect();
        return b.height > 0 && (b.bottom > box.bottom + 2 || b.top < box.top - 2 || b.right > box.right + 2 || b.left < box.left - 2);
      });
    }).map(cell => (cell.textContent ?? '').trim().slice(0, 60)));
  expect(clipped, `${name} has no clipped cell text`).toEqual([]);

  await page.screenshot({ path: testInfo.outputPath(`${name}.png`), fullPage: true });
}
