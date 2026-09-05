'use client';

import { useRef, useState } from 'react';
import Image from 'next/image';
import { motion, AnimatePresence } from 'framer-motion';
import { useLanguage } from '@/hooks/useLanguage';
import LanguageSwitcher from './LanguageSwitcher';
import ThemeToggle from './ThemeToggle';
import { getWhatsAppChatUrl, baseUrl } from '@/lib/whatsapp';
import type { Language } from '@/i18n';

interface NavbarProps {
  lang: Language;
}

// Removed 2026-08-25: 'nav.checklist' (#checklist section hidden) and
// 'nav.contact' (#contact — no such section exists on the page).
const navKeys = ['nav.services', 'nav.about', 'nav.faq'] as const;
const sectionIds = ['#services', '#about', '#faq'];

export default function Navbar({ lang }: NavbarProps) {
  const { t } = useLanguage();
  const [mobileOpen, setMobileOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);

  function toggleMenu() {
    const closing = mobileOpen;
    setMobileOpen(!closing);
    // The menu stays mounted (and links stay focusable) during the 250ms exit
    // animation — if focus was inside it, restore focus to the toggle button.
    if (closing && menuRef.current?.contains(document.activeElement)) {
      toggleRef.current?.focus();
    }
  }

  return (
    <nav className="sticky top-0 z-50 bg-white/90 dark:bg-accent-950/90 backdrop-blur-md border-b border-gray-100 dark:border-accent-900 shadow-sm">
      <div className="container-page">
        <div className="flex items-center justify-between h-16 lg:h-18">
          {/* Logo */}
          <a href={baseUrl(`/${lang}/`)} className="flex items-center gap-2 shrink-0">
            <Image
              src="/logo.webp"
              alt="ReadySetSiivous"
              width={40}
              height={40}
              priority
              className="w-10 h-10 rounded-full bg-white p-1 ring-1 ring-gray-200 dark:ring-gray-700"
            />
            <span translate="no" className="text-lg sm:text-xl font-extrabold text-gray-900 dark:text-gray-50 tracking-tight">
              ReadySet<span className="text-brand-600 dark:text-brand-400">Siivous</span>
            </span>
          </a>

          {/* Desktop nav links */}
          <div className="hidden lg:flex items-center gap-1">
            {navKeys.map((key, i) => (
              <a
                key={key}
                href={sectionIds[i]}
                className="px-4 py-2 text-sm font-medium text-gray-600 dark:text-gray-300 hover:text-brand-600 dark:hover:text-brand-400 rounded-lg hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors"
              >
                {t(key)}
              </a>
            ))}
            <a
              href={baseUrl(`/${lang}/varaus/`)}
              className="px-4 py-2 text-sm font-medium text-brand-600 dark:text-brand-400 hover:text-brand-700 dark:hover:text-brand-300 rounded-lg hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors"
            >
              {t('nav.booking')}
            </a>
            <a
              href={baseUrl(`/${lang}/card/`)}
              className="px-4 py-2 text-sm font-medium text-brand-600 dark:text-brand-400 hover:text-brand-700 dark:hover:text-brand-300 rounded-lg hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors"
            >
              📇 Card
            </a>
            <a
              href={baseUrl(`/${lang}/admin/`)}
              className="inline-flex items-center gap-2 px-4 py-2 text-sm font-semibold text-brand-600 dark:text-brand-400 border border-brand-300 dark:border-brand-700 rounded-full hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors"
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z" />
              </svg>
              {t('nav.admin')}
            </a>
          </div>

          {/* Right side */}
          <div className="flex items-center gap-2 sm:gap-3">
            <ThemeToggle />
            <LanguageSwitcher />

            <a
              href="tel:+358468044231"
              className="hidden xl:inline-flex items-center gap-2 px-3 py-2 text-sm font-semibold text-gray-700 dark:text-gray-200 hover:text-brand-700 dark:hover:text-brand-400 transition-colors"
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 5a2 2 0 012-2h3.28a1 1 0 01.948.684l1.498 4.493a1 1 0 01-.502 1.21l-2.257 1.13a11.042 11.042 0 005.516 5.516l1.13-2.257a1 1 0 011.21-.502l4.493 1.498a1 1 0 01.684.949V19a2 2 0 01-2 2h-1C9.716 21 3 14.284 3 6V5z" />
              </svg>
              {t('nav.callUs')}
            </a>

            <a
              href={getWhatsAppChatUrl()}
              target="_blank"
              rel="noopener noreferrer"
              className="hidden sm:inline-flex items-center gap-2 px-4 py-2 bg-whatsapp text-white text-sm font-semibold rounded-full hover:bg-whatsapp-dark transition-colors shadow-md shadow-green-200 dark:shadow-green-900/30"
            >
              <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24">
                <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413z"/>
              </svg>
              {t('nav.whatsappButton')}
            </a>

            <button
              ref={toggleRef}
              onClick={toggleMenu}
              className="lg:hidden p-2 rounded-lg text-gray-600 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors"
              aria-label="Toggle menu"
              aria-expanded={mobileOpen}
              aria-controls="mobile-menu"
            >
              <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                {mobileOpen ? (
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                ) : (
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
                )}
              </svg>
            </button>
          </div>
        </div>

        {/* Mobile menu */}
        <AnimatePresence>
          {mobileOpen && (
            <motion.div
              id="mobile-menu"
              ref={menuRef}
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{ duration: 0.25, ease: 'easeOut' }}
              className="lg:hidden border-t border-gray-100 dark:border-gray-800 overflow-hidden"
            >
              <div className="flex flex-col gap-1 pt-3 pb-4">
              {navKeys.map((key, i) => (
                <a
                  key={key}
                  href={sectionIds[i]}
                  onClick={() => setMobileOpen(false)}
                  className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-300 hover:text-brand-600 dark:hover:text-brand-400 rounded-lg hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors"
                >
                  {t(key)}
                </a>
              ))}
              <a
                href={baseUrl(`/${lang}/varaus/`)}
                onClick={() => setMobileOpen(false)}
                className="px-4 py-2.5 text-sm font-medium text-brand-600 dark:text-brand-400 hover:text-brand-700 rounded-lg hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors"
              >
                🧹 {t('nav.booking')}
              </a>
              <a
                href={baseUrl(`/${lang}/card/`)}
                onClick={() => setMobileOpen(false)}
                className="px-4 py-2.5 text-sm font-medium text-brand-600 dark:text-brand-400 hover:text-brand-700 rounded-lg hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors"
              >
                📇 {lang === 'fi' ? 'Käyntikortti' : lang === 'sv' ? 'Visitkort' : 'Business Card'}
              </a>
              <a
                href={baseUrl(`/${lang}/admin/`)}
                onClick={() => setMobileOpen(false)}
                className="px-4 py-2.5 text-sm font-medium text-brand-600 dark:text-brand-400 hover:text-brand-700 rounded-lg hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors"
              >
                🔐 {t('nav.admin')}
              </a>
              <a
                href="tel:+358468044231"
                onClick={() => setMobileOpen(false)}
                className="px-4 py-2.5 text-sm font-medium text-gray-600 dark:text-gray-300 hover:text-brand-600 dark:hover:text-brand-400 rounded-lg hover:bg-brand-50 dark:hover:bg-brand-500/10 transition-colors"
              >
                📞 {t('nav.callUs')}
              </a>
              <a
                href={getWhatsAppChatUrl()}
                target="_blank"
                rel="noopener noreferrer"
                onClick={() => setMobileOpen(false)}
                className="sm:hidden mt-2 flex items-center justify-center gap-2 px-4 py-2.5 bg-whatsapp text-white text-sm font-semibold rounded-full hover:bg-whatsapp-dark transition-colors"
              >
                <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413z"/>
                </svg>
                {t('nav.whatsappButton')}
              </a>
            </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </nav>
  );
}
