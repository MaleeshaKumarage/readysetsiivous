'use client';

import { useEffect } from 'react';

/**
 * Root redirect page.
 * Detects browser language or reads localStorage and redirects
 * to the correct language subpath, accounting for the GitHub Pages basePath.
 */
export default function RootPage() {
  useEffect(() => {
    const base = process.env.NEXT_PUBLIC_BASE_PATH || '';

    try {
      const stored = localStorage.getItem('lang');
      if (stored === 'en' || stored === 'fi' || stored === 'sv') {
        window.location.replace(`${base}/${stored}/`);
        return;
      }
      const browser = navigator.language.slice(0, 2);
      if (browser === 'sv') window.location.replace(`${base}/sv/`);
      else if (browser === 'en') window.location.replace(`${base}/en/`);
      else window.location.replace(`${base}/fi/`);
    } catch {
      window.location.replace(`${base}/fi/`);
    }
  }, []);

  return null;
}
