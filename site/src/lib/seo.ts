import type { Metadata } from 'next';

export const SITE_URL = 'https://readysetsiivous.fi';

/** Keeps a page out of search results and stops crawlers following its links. */
export const NOINDEX: NonNullable<Metadata['robots']> = { index: false, follow: false };

/**
 * Canonical and language alternates for a public page. `path` is the page path after the
 * language segment, with a trailing slash, e.g. '/varaus/'. Every language points at its own
 * version of the page, so a sub-page never declares the language homepage as its canonical.
 */
export function localizedAlternates(lang: string, path: string): NonNullable<Metadata['alternates']> {
  const at = (language: string) => `${SITE_URL}/${language}${path}`;
  return {
    canonical: at(lang),
    languages: { fi: at('fi'), en: at('en'), sv: at('sv'), 'x-default': at('fi') },
  };
}
