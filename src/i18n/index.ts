import en from './en.json';
import fi from './fi.json';
import sv from './sv.json';

export type Language = 'en' | 'fi' | 'sv';

export const LANGUAGES: { code: Language; label: string; flag: string }[] = [
  { code: 'en', label: 'English', flag: '🇬🇧' },
  { code: 'fi', label: 'Suomi', flag: '🇫🇮' },
  { code: 'sv', label: 'Svenska', flag: '🇸🇪' },
];

export const DEFAULT_LANGUAGE: Language = 'fi';

const dictionaries: Record<Language, typeof en> = { en, fi, sv };

/**
 * Recursively resolve a dot-path like "nav.services" against a dictionary.
 */
export function t(
  lang: Language,
  path: string,
  fallback?: string
): string {
  const dict = dictionaries[lang] ?? dictionaries[DEFAULT_LANGUAGE];
  const keys = path.split('.');
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let value: any = dict;

  for (const key of keys) {
    if (value == null || typeof value !== 'object') {
      return fallback ?? path;
    }
    value = value[key];
  }

  if (typeof value === 'string') return value;
  return fallback ?? path;
}

/**
 * Retrieve a nested array from the dictionary (e.g. footer.areas).
 */
export function ta(lang: Language, path: string): string[] {
  const dict = dictionaries[lang] ?? dictionaries[DEFAULT_LANGUAGE];
  const keys = path.split('.');
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let value: any = dict;

  for (const key of keys) {
    if (value == null || typeof value !== 'object') return [];
    value = value[key];
  }

  if (Array.isArray(value)) return value;
  return [];
}

/**
 * Retrieve a nested object from the dictionary (e.g. services.home).
 */
export function to(
  lang: Language,
  path: string
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
): Record<string, any> | null {
  const dict = dictionaries[lang] ?? dictionaries[DEFAULT_LANGUAGE];
  const keys = path.split('.');
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let value: any = dict;

  for (const key of keys) {
    if (value == null || typeof value !== 'object') return null;
    value = value[key];
  }

  if (typeof value === 'object' && !Array.isArray(value)) return value;
  return null;
}

export function getDictionary(lang: Language): typeof en {
  return dictionaries[lang] ?? dictionaries[DEFAULT_LANGUAGE];
}
