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
 * Pre-flatten nested translation objects into a flat key-value dictionary at module load time.
 * E.g., { "nav": { "services": "Services" } } resolves "nav.services" directly to "Services".
 * This enables O(1) direct property access in t(), ta(), and to(), avoiding path.split('.')
 * allocations and object iteration overhead on every translation call across components.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function flattenDictionary(obj: Record<string, any>, prefix = ''): Record<string, any> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const res: Record<string, any> = {};
  for (const k in obj) {
    if (!Object.prototype.hasOwnProperty.call(obj, k)) continue;
    const val = obj[k];
    const newKey = prefix ? `${prefix}.${k}` : k;
    res[newKey] = val;
    if (val && typeof val === 'object' && !Array.isArray(val)) {
      Object.assign(res, flattenDictionary(val, newKey));
    }
  }
  return res;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const flatDictionaries: Record<Language, Record<string, any>> = {
  en: flattenDictionary(en),
  fi: flattenDictionary(fi),
  sv: flattenDictionary(sv),
};

/**
 * Recursively resolve a dot-path like "nav.services" against a dictionary.
 * Uses pre-flattened O(1) dictionary lookups to eliminate string splitting and traversal loops.
 */
export function t(
  lang: Language,
  path: string,
  fallback?: string
): string {
  const dict = flatDictionaries[lang] ?? flatDictionaries[DEFAULT_LANGUAGE];
  const value = dict[path];
  if (typeof value === 'string') return value;
  return fallback ?? path;
}

/**
 * Retrieve a nested array from the dictionary (e.g. footer.areas).
 * Uses pre-flattened O(1) dictionary lookups.
 */
export function ta(lang: Language, path: string): string[] {
  const dict = flatDictionaries[lang] ?? flatDictionaries[DEFAULT_LANGUAGE];
  const value = dict[path];
  if (Array.isArray(value)) return value;
  return [];
}

/**
 * Retrieve a nested object from the dictionary (e.g. services.home).
 * Uses pre-flattened O(1) dictionary lookups.
 */
export function to(
  lang: Language,
  path: string
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
): Record<string, any> | null {
  const dict = flatDictionaries[lang] ?? flatDictionaries[DEFAULT_LANGUAGE];
  const value = dict[path];
  if (typeof value === 'object' && value !== null && !Array.isArray(value)) return value;
  return null;
}

export function getDictionary(lang: Language): typeof en {
  return dictionaries[lang] ?? dictionaries[DEFAULT_LANGUAGE];
}
