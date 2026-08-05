'use client';

import React, { createContext, useCallback, useEffect, useState } from 'react';
import { type Language, DEFAULT_LANGUAGE, LANGUAGES } from '@/i18n';

interface LanguageContextValue {
  lang: Language;
  setLang: (lang: Language) => void;
  languages: typeof LANGUAGES;
}

export const LanguageContext = createContext<LanguageContextValue>({
  lang: DEFAULT_LANGUAGE,
  setLang: () => {},
  languages: LANGUAGES,
});

export function LanguageProvider({ children }: { children: React.ReactNode }) {
  const [lang, setLangState] = useState<Language>(DEFAULT_LANGUAGE);

  useEffect(() => {
    // Hydrate from localStorage on mount
    try {
      const stored = localStorage.getItem('lang');
      if (stored && LANGUAGES.some((l) => l.code === stored)) {
        setLangState(stored as Language);
      } else {
        // Attempt browser-language detection
        const browserLang = navigator.language.slice(0, 2);
        const match = LANGUAGES.find((l) => l.code === browserLang);
        if (match) setLangState(match.code);
      }
    } catch {
      // localStorage not available
    }
  }, []);

  const setLang = useCallback((next: Language) => {
    setLangState(next);
    try {
      localStorage.setItem('lang', next);
    } catch {
      // ignore
    }
    // Set html lang attribute
    document.documentElement.lang = next;
  }, []);

  useEffect(() => {
    document.documentElement.lang = lang;
  }, [lang]);

  return (
    <LanguageContext.Provider value={{ lang, setLang, languages: LANGUAGES }}>
      {children}
    </LanguageContext.Provider>
  );
}
