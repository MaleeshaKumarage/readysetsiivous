// Pure mapping from the services editor form to the admin service payload.
// Editing one displayed field must not rewrite stored translations, images, icons, extra info, order or featured flags.
import type { ServiceFields } from './adminApi';

export const DEFAULT_VAT_RATE_PERCENT = 25.5;
export const DEFAULT_DURATION_MINUTES = 60;
export const MIN_DURATION_MINUTES = 15;

export type StoredService = {
  slug: string;
  category: string;
  name: { values: Record<string, string> };
  description: { values: Record<string, string> };
  additionalInfo: { values: Record<string, string> } | null;
  icon: string;
  imageUrl?: string | null;
  currency?: string;
  durationMinutes: number;
  priceNet: number;
  vatRatePercent: number;
  isFeatured: boolean;
  sortOrder: number;
};

export type ServiceForm = {
  slug: string;
  nameFi: string; nameEn: string; nameSv: string;
  descFi: string; descEn: string; descSv: string;
  priceNet: string; vatRatePercent: string; durationMinutes: string;
  category: string;
};

/** Blank means "not entered" and takes the default. An entered 0 stays 0. */
export function parseVatRate(input: string): number {
  const text = input.trim();
  if (text === '') return DEFAULT_VAT_RATE_PERCENT;
  const value = Number(text);
  return Number.isFinite(value) && value >= 0 ? value : DEFAULT_VAT_RATE_PERCENT;
}

export function parsePrice(input: string): number {
  const value = Number(input.trim());
  return Number.isFinite(value) && value >= 0 ? value : 0;
}

export function parseDuration(input: string): number {
  const value = Number(input.trim());
  if (!Number.isFinite(value) || value <= 0) return DEFAULT_DURATION_MINUTES;
  return Math.max(MIN_DURATION_MINUTES, value);
}

/**
 * Starts from the stored translations, so languages the form does not show are kept.
 * fi is always written. en and sv are set when entered and removed when cleared.
 */
export function mergeLocalized(stored: Record<string, string> | undefined, fi: string, en: string, sv: string): Record<string, string> {
  const merged: Record<string, string> = { ...(stored ?? {}) };
  merged.fi = fi.trim();
  for (const [key, value] of [['en', en], ['sv', sv]] as const) {
    if (value.trim()) merged[key] = value.trim();
    else delete merged[key];
  }
  return merged;
}

/** Full payload for an update. Anything the form does not edit is copied from the stored record. */
export function serviceFieldsFromForm(stored: StoredService | null, form: ServiceForm): ServiceFields {
  return {
    slug: form.slug.trim(),
    category: form.category,
    name: mergeLocalized(stored?.name.values, form.nameFi, form.nameEn, form.nameSv),
    description: mergeLocalized(stored?.description.values, form.descFi, form.descEn, form.descSv),
    additionalInfo: stored?.additionalInfo?.values ?? null,
    durationMinutes: parseDuration(form.durationMinutes),
    priceNet: parsePrice(form.priceNet),
    vatRatePercent: parseVatRate(form.vatRatePercent),
    currency: stored?.currency ?? 'EUR',
    icon: stored?.icon ?? 'Sparkles',
    imageUrl: stored?.imageUrl ?? null,
    sortOrder: stored?.sortOrder ?? 0,
    isFeatured: stored?.isFeatured ?? false,
  };
}
