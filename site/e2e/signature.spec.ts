import { test, expect } from '@playwright/test';

const STORY = '/iframe.html?id=components-signaturepad--draw';

test('canvas has touch-action none', async ({ page }) => {
  await page.goto(STORY);
  const canvas = page.locator('canvas');
  await expect(canvas).toBeVisible();
  const ta = await canvas.evaluate((c) => getComputedStyle(c).touchAction);
  expect(ta).toBe('none');
});

test('no connector line between strokes', async ({ page }) => {
  await page.goto(STORY);
  const canvas = page.locator('canvas');
  await expect(canvas).toBeVisible();

  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();
  const x1 = box!.x + 30;
  const x2 = box!.x + 80;
  const yTop = box!.y + 30;   // first stroke row
  const yBot = box!.y + 100;  // second stroke row
  const yGap = box!.y + 60;   // gap row between them

  // Stroke 1: top horizontal
  await page.mouse.move(x1, yTop);
  await page.mouse.down();
  await page.mouse.move(x2, yTop, { steps: 10 });
  await page.mouse.up();

  // Lift and move away before second stroke
  await page.mouse.move(x1, yBot, { steps: 10 });

  // Stroke 2: bottom horizontal
  await page.mouse.down();
  await page.mouse.move(x2, yBot, { steps: 10 });
  await page.mouse.up();

  const rows = await canvas.evaluate((c) => {
    const ctx = (c as HTMLCanvasElement).getContext('2d')!;
    const w = c.width, h = c.height;
    const gapY = 60; // CSS px; canvas already DPR-scaled so sample near middle
    const dpr = window.devicePixelRatio || 1;
    const gap = Math.round(gapY * dpr);
    const y = Math.min(gap, h - 1);
    const data = ctx.getImageData(0, y, w, 1).data;
    let ink = 0;
    for (let x = 0; x < w; x++) {
      if (data[x * 4 + 3] > 0) ink++;
    }
    return { ink, y, w };
  });

  // Gap row must be blank — no connector line between the two strokes.
  expect(rows.ink).toBe(0);
});

test('touch draw produces ink without page scroll', async ({ page }) => {
  test.skip(!test.info().project.name.includes('mobile'), 'mobile project only');
  await page.goto(STORY);
  const canvas = page.locator('canvas');
  await expect(canvas).toBeVisible();

  const box = await canvas.boundingBox();
  expect(box).not.toBeNull();

  const client = await page.context().newCDPSession(page);
  const startX = box!.x + 30, endX = box!.x + 80, y = box!.y + 50;

  await client.send('Input.dispatchTouchEvent', {
    type: 'touchStart',
    touchPoints: [{ x: startX, y, radiusX: 1, radiusY: 1 }],
  });
  for (let i = 1; i <= 10; i++) {
    await client.send('Input.dispatchTouchEvent', {
      type: 'touchMove',
      touchPoints: [{ x: startX + (endX - startX) * (i / 10), y, radiusX: 1, radiusY: 1 }],
    });
  }
  await client.send('Input.dispatchTouchEvent', {
    type: 'touchEnd',
    touchPoints: [],
  });

  // Ink must exist (drawing worked) and scrollY must be unchanged (touch not stolen).
  const result = await canvas.evaluate((c) => {
    const ctx = (c as HTMLCanvasElement).getContext('2d')!;
    const d = ctx.getImageData(0, 0, c.width, c.height).data;
    let ink = 0;
    for (let i = 3; i < d.length; i += 4) if (d[i] > 0) ink++;
    return { ink, scrollY: window.scrollY };
  });

  expect(result.ink).toBeGreaterThan(0);
  expect(result.scrollY).toBe(0);
});
