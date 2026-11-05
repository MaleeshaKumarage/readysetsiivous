import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULT_VAT_RATE_PERCENT, mergeLocalized, parseDuration, parsePrice, parseVatRate, serviceFieldsFromForm, type ServiceForm, type StoredService } from './serviceFields.ts';

const stored: StoredService = {
  slug: 'deep-clean', category: 'cleaning',
  name: { values: { fi: 'Syväpuhdistus', en: 'Deep clean', sv: 'Djupstädning', de: 'Tiefenreinigung' } },
  description: { values: { fi: 'Kuvaus', en: 'Description', de: 'Beschreibung' } },
  additionalInfo: { values: { fi: 'Lisätiedot' } },
  icon: 'Broom', imageUrl: '/uploads/deep.webp', currency: 'EUR',
  durationMinutes: 180, priceNet: 120, vatRatePercent: 0, isFeatured: true, sortOrder: 7,
};

const formFrom = (record: StoredService): ServiceForm => ({
  slug: record.slug, nameFi: record.name.values.fi, nameEn: record.name.values.en ?? '', nameSv: record.name.values.sv ?? '',
  descFi: record.description.values.fi, descEn: record.description.values.en ?? '', descSv: '',
  priceNet: String(record.priceNet), vatRatePercent: String(record.vatRatePercent), durationMinutes: String(record.durationMinutes), category: record.category,
});

test('zero VAT stays zero instead of becoming the default', () => {
  assert.equal(parseVatRate('0'), 0);
  assert.equal(parseVatRate(' 0.0 '), 0);
});

test('blank or invalid VAT takes the explicit default', () => {
  assert.equal(parseVatRate(''), DEFAULT_VAT_RATE_PERCENT);
  assert.equal(parseVatRate('   '), DEFAULT_VAT_RATE_PERCENT);
  assert.equal(parseVatRate('abc'), DEFAULT_VAT_RATE_PERCENT);
  assert.equal(parseVatRate('-5'), DEFAULT_VAT_RATE_PERCENT);
});

test('entered VAT values are kept', () => {
  assert.equal(parseVatRate('10'), 10);
  assert.equal(parseVatRate('25.5'), 25.5);
});

test('price is never negative and blank is zero', () => {
  assert.equal(parsePrice(''), 0);
  assert.equal(parsePrice('-3'), 0);
  assert.equal(parsePrice('49.9'), 49.9);
});

test('duration has a 15 minute floor and a 60 minute default', () => {
  assert.equal(parseDuration(''), 60);
  assert.equal(parseDuration('0'), 60);
  assert.equal(parseDuration('5'), 15);
  assert.equal(parseDuration('90'), 90);
});

test('mergeLocalized keeps languages the form does not show', () => {
  const merged = mergeLocalized({ fi: 'Vanha', de: 'Tiefenreinigung' }, 'Uusi', '', '');
  assert.deepEqual(merged, { fi: 'Uusi', de: 'Tiefenreinigung' });
});

test('mergeLocalized writes entered translations and removes cleared ones', () => {
  const merged = mergeLocalized({ fi: 'A', en: 'Old', sv: 'Gammal' }, ' A ', 'New', '  ');
  assert.deepEqual(merged, { fi: 'A', en: 'New' });
});

test('editing one field preserves every unedited stored field', () => {
  const form = { ...formFrom(stored), priceNet: '150' };
  const fields = serviceFieldsFromForm(stored, form);
  assert.equal(fields.priceNet, 150);
  assert.deepEqual(fields.name, { fi: 'Syväpuhdistus', en: 'Deep clean', sv: 'Djupstädning', de: 'Tiefenreinigung' });
  assert.deepEqual(fields.description, { fi: 'Kuvaus', en: 'Description', de: 'Beschreibung' });
  assert.deepEqual(fields.additionalInfo, { fi: 'Lisätiedot' });
  assert.equal(fields.icon, 'Broom');
  assert.equal(fields.imageUrl, '/uploads/deep.webp');
  assert.equal(fields.currency, 'EUR');
  assert.equal(fields.sortOrder, 7);
  assert.equal(fields.isFeatured, true);
  assert.equal(fields.durationMinutes, 180);
});

test('saving a zero-VAT service keeps VAT at zero', () => {
  const fields = serviceFieldsFromForm(stored, formFrom(stored));
  assert.equal(fields.vatRatePercent, 0);
});

test('creating a service uses explicit defaults', () => {
  const fields = serviceFieldsFromForm(null, { slug: 'new', nameFi: 'Uusi', nameEn: '', nameSv: '', descFi: '', descEn: '', descSv: '', priceNet: '', vatRatePercent: '', durationMinutes: '', category: 'cleaning' });
  assert.equal(fields.vatRatePercent, 25.5);
  assert.equal(fields.durationMinutes, 60);
  assert.equal(fields.priceNet, 0);
  assert.equal(fields.icon, 'Sparkles');
  assert.equal(fields.currency, 'EUR');
  assert.equal(fields.sortOrder, 0);
  assert.equal(fields.isFeatured, false);
  assert.equal(fields.additionalInfo, null);
  assert.equal(fields.imageUrl, null);
});
