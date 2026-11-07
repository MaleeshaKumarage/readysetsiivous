import test from 'node:test';
import assert from 'node:assert/strict';
import { money, gross, helsinkiToday, fullDate, locale } from './publicFormatting.ts';

test('locale mapping returns expected BCP-47 identifiers', () => {
  assert.equal(locale('fi'), 'fi-FI');
  assert.equal(locale('en'), 'en-GB');
  assert.equal(locale('sv'), 'sv-FI');
});

test('money formats currency correctly across languages', () => {
  const fiFormatted = money(12.5, 'fi');
  assert.match(fiFormatted, /12,50\s*€/);

  const enFormatted = money(12.5, 'en');
  assert.match(enFormatted, /€12\.50/);

  const svFormatted = money(12.5, 'sv');
  assert.match(svFormatted, /12,50\s*€/);
});

test('gross computes price net + VAT correctly when gross is null', () => {
  const result = gross({
    id: '1',
    name: 'Home Cleaning',
    slug: 'home-cleaning',
    category: 'residential',
    priceNet: 100,
    priceGross: null,
    vatRatePercent: 25.5,
  } as any);
  assert.equal(result, 125.5);
});

test('gross returns priceGross when present', () => {
  const result = gross({
    id: '1',
    name: 'Home Cleaning',
    slug: 'home-cleaning',
    category: 'residential',
    priceNet: 100,
    priceGross: 120,
    vatRatePercent: 25.5,
  } as any);
  assert.equal(result, 120);
});

test('helsinkiToday returns YYYY-MM-DD format', () => {
  const dateStr = helsinkiToday();
  assert.match(dateStr, /^\d{4}-\d{2}-\d{2}$/);
});

test('fullDate formats ISO date correctly for each language with caching', () => {
  const fi = fullDate('2026-11-07', 'fi');
  const en = fullDate('2026-11-07', 'en');
  const sv = fullDate('2026-11-07', 'sv');

  // English output (en-GB includes comma: 'Saturday, 7 November 2026')
  assert.equal(en, 'Saturday, 7 November 2026');

  // Finnish output contains day, month, year
  assert.match(fi, /lauantaina 7\. marraskuuta 2026/i);

  // Swedish output contains day, month, year
  assert.match(sv, /lördag 7 november 2026/i);

  // Second call uses cached formatter and returns identical results
  assert.equal(fullDate('2026-11-07', 'en'), 'Saturday, 7 November 2026');
});
