'use client';

import { useEffect } from 'react';

/**
 * Root-URL redirect: honour an explicitly chosen language from localStorage,
 * otherwise go to Finnish (the site's default language).
 * The server-rendered fallback (language links) stays visible for
 * crawlers and users without JavaScript.
 */
export default function LanguageRedirect() {
  useEffect(() => {
    try {
      const stored = localStorage.getItem('lang');
      if (stored === 'en' || stored === 'fi' || stored === 'sv') {
        window.location.replace(`/${stored}/`);
        return;
      }
      // Finnish is the site's default language.
      window.location.replace('/fi/');
    } catch {
      window.location.replace('/fi/');
    }
  }, []);

  return null;
}
