import { test } from 'node:test';
import assert from 'node:assert/strict';
import { helsinkiToday, fullDate, money, gross, locale } from './publicFormatting.ts';

test('locale maps supported languages to expected BCP 47 tags', () => {
  assert.equal(locale('fi'), 'fi-FI');
  assert.equal(locale('en'), 'en-GB');
  assert.equal(locale('sv'), 'sv-FI');
});

test('helsinkiToday returns current date in YYYY-MM-DD format', () => {
  const today = helsinkiToday();
  assert.match(today, /^\d{4}-\d{2}-\d{2}$/);
});

test('fullDate formats dates consistently across languages using cached formatters', () => {
  const dateStr = '2026-06-15';
  const fiFormatted = fullDate(dateStr, 'fi');
  const enFormatted = fullDate(dateStr, 'en');
  const svFormatted = fullDate(dateStr, 'sv');

  // Verify that repeated calls return identical results (exercising the cache)
  assert.equal(fullDate(dateStr, 'fi'), fiFormatted);
  assert.equal(fullDate(dateStr, 'en'), enFormatted);
  assert.equal(fullDate(dateStr, 'sv'), svFormatted);

  assert.ok(fiFormatted.includes('kesäkuuta') || fiFormatted.includes('maanantaina'));
  assert.ok(enFormatted.includes('June') || enFormatted.includes('Monday'));
  assert.ok(svFormatted.includes('juni') || svFormatted.includes('måndag'));
});

test('money formats currency values and reuses formatters', () => {
  const formattedFi = money(50, 'fi');
  assert.ok(formattedFi.includes('50') && formattedFi.includes('€'));
  assert.equal(money(50, 'fi'), formattedFi);
});

test('gross calculates gross price with VAT correctly', () => {
  const service = {
    id: 's1',
    name: 'Home Cleaning',
    slug: 'home-cleaning',
    priceNet: 100,
    vatRatePercent: 25.5,
    description: '',
    durationMinutes: 120,
    category: 'regular',
  };
  assert.equal(gross(service as any), 125.5);
});
