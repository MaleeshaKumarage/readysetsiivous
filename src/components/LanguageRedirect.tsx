'use client';

import { useEffect } from 'react';

/**
 * Client-side language detection for the root URL.
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
      const browser = navigator.language.slice(0, 2);
      if (browser === 'sv') window.location.replace('/sv/');
      else if (browser === 'en') window.location.replace('/en/');
      else window.location.replace('/fi/');
    } catch {
      window.location.replace('/fi/');
    }
  }, []);

  return null;
}
