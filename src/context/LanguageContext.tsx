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

export function LanguageProvider({
  children,
  initialLang,
}: {
  children: React.ReactNode;
  initialLang?: Language;
}) {
  const [lang, setLangState] = useState<Language>(initialLang ?? DEFAULT_LANGUAGE);

  useEffect(() => {
    if (initialLang) {
      setLangState(initialLang);
    }
  }, [initialLang]);

  useEffect(() => {
    document.documentElement.lang = lang;
    try {
      localStorage.setItem('lang', lang);
    } catch {
      // ignore
    }
  }, [lang]);

  const setLang = useCallback((next: Language) => {
    setLangState(next);
  }, []);

  // Sync with URL path — if we're on a different language path than what's in state, update
  useEffect(() => {
    try {
      const pathMatch = window.location.pathname.match(/^\/(en|fi|sv)\//);
      if (pathMatch) {
        const urlLang = pathMatch[1] as Language;
        if (urlLang !== lang) {
          setLangState(urlLang);
        }
      }
    } catch {
      // ignore
    }
  }, []);

  return (
    <LanguageContext.Provider value={{ lang, setLang, languages: LANGUAGES }}>
      {children}
    </LanguageContext.Provider>
  );
}
