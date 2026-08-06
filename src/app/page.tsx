'use client';

import { useEffect } from 'react';

export default function RootPage() {
  useEffect(() => {
    // Detect browser language or default to Finnish
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
