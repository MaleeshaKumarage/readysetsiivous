'use client';

import { useContext, useCallback, useMemo } from 'react';
import { LanguageContext } from '@/context/LanguageContext';
import { t, ta, to } from '@/i18n';

export function useLanguage() {
  const { lang, setLang, languages } = useContext(LanguageContext);

  /**
   * Translate: dot-path access (e.g. "hero.headline").
   * Falls back to the path string if the key isn't found.
   * Memoized with useCallback to avoid function recreation on every render.
   */
  const translate = useCallback(
    (path: string, fallback?: string): string => {
      return t(lang, path, fallback);
    },
    [lang]
  );

  /**
   * Translate and return an array (e.g. footer.areas).
   * Memoized with useCallback to avoid function recreation on every render.
   */
  const translateArray = useCallback(
    (path: string): string[] => {
      return ta(lang, path);
    },
    [lang]
  );

  /**
   * Translate and return a nested object (e.g. services.home).
   * Memoized with useCallback to avoid function recreation on every render.
   */
  const translateObject = useCallback(
    (path: string): Record<string, unknown> | null => {
      return to(lang, path);
    },
    [lang]
  );

  // Return a memoized context object so consumer components using useLanguage()
  // retain stable function references and do not receive a new object identity unless `lang` changes.
  return useMemo(
    () => ({
      lang,
      setLang,
      languages,
      t: translate,
      ta: translateArray,
      to: translateObject,
    }),
    [lang, setLang, languages, translate, translateArray, translateObject]
  );
}
