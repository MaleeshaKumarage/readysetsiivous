'use client';

import { useContext } from 'react';
import { LanguageContext } from '@/context/LanguageContext';
import { t, ta, to } from '@/i18n';

export function useLanguage() {
  const { lang, setLang, languages } = useContext(LanguageContext);

  /**
   * Translate: dot-path access (e.g. "hero.headline").
   * Falls back to the path string if the key isn't found.
   */
  const translate = (path: string, fallback?: string): string => {
    return t(lang, path, fallback);
  };

  /**
   * Translate and return an array (e.g. footer.areas).
   */
  const translateArray = (path: string): string[] => {
    return ta(lang, path);
  };

  /**
   * Translate and return a nested object (e.g. services.home).
   */
  const translateObject = (path: string): Record<string, unknown> | null => {
    return to(lang, path);
  };

  return {
    lang,
    setLang,
    languages,
    t: translate,
    ta: translateArray,
    to: translateObject,
  };
}
