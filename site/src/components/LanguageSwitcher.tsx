'use client';

import { useState, useRef, useEffect, useCallback } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { useLanguage } from '@/hooks/useLanguage';

// Keep this transient interaction across the localized layout remount.
let pendingFocusPath: string | null = null;

export default function LanguageSwitcher() {
  const { lang, setLang, languages } = useLanguage();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const optionRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (pendingFocusPath === pathname) {
      pendingFocusPath = null;
      buttonRef.current?.focus();
    }
  }, [pathname]);

  const current = languages.find((l) => l.code === lang) ?? languages[0];

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, []);

  useEffect(() => {
    if (!open) return;

    // Move focus to currently selected language or first option when menu opens
    const currentIdx = languages.findIndex((l) => l.code === lang);
    const targetIdx = currentIdx >= 0 ? currentIdx : 0;
    optionRefs.current[targetIdx]?.focus();

    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setOpen(false);
        buttonRef.current?.focus();
      }
    }
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [open, languages, lang]);

  const handleOptionKeyDown = (e: React.KeyboardEvent, index: number) => {
    const total = languages.length;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      optionRefs.current[(index + 1) % total]?.focus();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      optionRefs.current[(index - 1 + total) % total]?.focus();
    } else if (e.key === 'Home') {
      e.preventDefault();
      optionRefs.current[0]?.focus();
    } else if (e.key === 'End') {
      e.preventDefault();
      optionRefs.current[total - 1]?.focus();
    }
  };

  const switchTo = useCallback(
    (code: 'en' | 'fi' | 'sv') => {
      setLang(code);
      setOpen(false);
      buttonRef.current?.focus();
      // Client-side navigation to the new language URL path (no full reload).
      const newPath = window.location.pathname.replace(/^\/(en|fi|sv)\//, `/${code}/`);
      if (newPath !== window.location.pathname) {
        pendingFocusPath = newPath;
        router.push(newPath + window.location.search + window.location.hash);
      }
    },
    [setLang, router]
  );

  const ariaLabel =
    lang === 'fi' ? 'Vaihda kieltä' : lang === 'sv' ? 'Byt språk' : 'Switch language';
  const menuLabel =
    lang === 'fi' ? 'Kielet' : lang === 'sv' ? 'Språk' : 'Languages';

  return (
    <div className="relative" ref={ref}>
      <button
        ref={buttonRef}
        onClick={() => setOpen(!open)}
        className="flex items-center gap-1.5 px-3 py-2 rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 text-sm font-medium text-gray-700 dark:text-gray-200 hover:border-brand-400 dark:hover:border-brand-500 hover:text-brand-700 dark:hover:text-brand-400 focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 focus:outline-none transition-colors"
        aria-label={ariaLabel}
        aria-haspopup="menu"
        aria-controls="language-options"
        aria-expanded={open}
      >
        <span className="font-semibold">{current.code.toUpperCase()}</span>
        <span className="hidden sm:inline">{current.label}</span>
        <svg
          className={`w-4 h-4 transition-transform ${open ? 'rotate-180' : ''}`}
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {open && (
        <div
          id="language-options"
          role="menu"
          aria-orientation="vertical"
          aria-label={menuLabel}
          className="absolute right-0 mt-2 w-40 bg-white dark:bg-gray-800 border border-gray-100 dark:border-gray-700 rounded-xl shadow-lg dark:shadow-gray-950/50 overflow-hidden z-50 animate-fade-in-up"
        >
          {languages.map((l, index) => (
            <button
              key={l.code}
              ref={(el) => { optionRefs.current[index] = el; }}
              role="menuitemradio"
              aria-checked={l.code === lang}
              onClick={() => switchTo(l.code)}
              onKeyDown={(e) => handleOptionKeyDown(e, index)}
              className={`w-full flex items-center gap-2 px-4 py-2.5 text-sm text-left transition-colors hover:bg-brand-50 dark:hover:bg-brand-500/10 focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-inset focus:outline-none ${
                l.code === lang
                  ? 'bg-brand-50 dark:bg-brand-500/10 text-brand-700 dark:text-brand-400 font-semibold'
                  : 'text-gray-700 dark:text-gray-200'
              }`}
            >
              <span aria-hidden="true">{l.code.toUpperCase()}</span>
              <span>{l.label}</span>
              {l.code === lang && (
                <svg className="w-4 h-4 ml-auto text-brand-500 dark:text-brand-400" fill="currentColor" viewBox="0 0 20 20">
                  <path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clipRule="evenodd" />
                </svg>
              )}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
