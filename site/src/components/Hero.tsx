'use client';

import { useEffect, useState } from 'react';
import { motion } from 'framer-motion';
import { useLanguage } from '@/hooks/useLanguage';
import { getWhatsAppChatUrl, baseUrl } from '@/lib/whatsapp';
import TrustBadges from './TrustBadges';

/** SSR-safe matchMedia helper (framer-motion v13 does not export useMediaQuery). */
function useMinWidth(query: string): boolean {
  const [matches, setMatches] = useState(false);
  useEffect(() => {
    const mq = window.matchMedia(query);
    setMatches(mq.matches);
    const handler = (e: MediaQueryListEvent) => setMatches(e.matches);
    mq.addEventListener('change', handler);
    return () => mq.removeEventListener('change', handler);
  }, [query]);
  return matches;
}

const heroContainer = {
  hidden: {},
  show: { transition: { staggerChildren: 0.12, delayChildren: 0.1 } },
};

const heroItem = {
  hidden: { opacity: 0, y: 24 },
  show: { opacity: 1, y: 0, transition: { duration: 0.6, ease: 'easeOut' as const } },
};

export default function Hero() {
  const { t } = useLanguage();
  // The visual column is `hidden lg:flex` — the infinite float must not run
  // (and burn rAF frames) on viewports where the element is display:none.
  const isDesktop = useMinWidth('(min-width: 1024px)');

  return (
    <section className="relative overflow-hidden bg-gradient-to-br from-brand-50 via-white to-accent-50 dark:from-gray-950 dark:via-gray-950 dark:to-gray-900">
      {/* Background decorative blobs */}
      <div className="absolute top-0 right-0 -translate-y-1/3 translate-x-1/4 w-[500px] h-[500px] rounded-full bg-brand-200/30 dark:bg-brand-500/10 blur-3xl pointer-events-none" />
      <div className="absolute bottom-0 left-0 translate-y-1/3 -translate-x-1/4 w-[400px] h-[400px] rounded-full bg-accent-200/25 dark:bg-accent-500/8 blur-3xl pointer-events-none" />

      <div className="container-page relative">
        <motion.div
          className="section-padding flex flex-col lg:flex-row items-center gap-12 lg:gap-16"
          variants={heroContainer}
          initial="hidden"
          animate="show"
        >
          {/* Text column */}
          <div className="flex-1 text-center lg:text-left max-w-2xl">
            <motion.h1 data-motion-reveal variants={heroItem} className="heading-xl text-gray-900 dark:text-gray-50 text-balance">
              {t('hero.headline')}
            </motion.h1>
            <motion.p data-motion-reveal variants={heroItem} className="mt-6 text-lg sm:text-xl text-gray-600 dark:text-gray-300 leading-relaxed text-balance">
              {t('hero.subheadline')}
            </motion.p>

            <motion.p
              data-motion-reveal
              variants={heroItem}
              className="mt-4 inline-flex items-center gap-2 text-sm font-semibold text-brand-600 dark:text-brand-400"
            >
              <span className="relative flex h-2.5 w-2.5">
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-brand-400 opacity-60" />
                <span className="relative inline-flex rounded-full h-2.5 w-2.5 bg-brand-500" />
              </span>
              {t('hero.sameDay')}
            </motion.p>

            {/* CTAs */}
            <motion.div data-motion-reveal variants={heroItem} className="mt-8 flex flex-col gap-4">
              <div className="flex flex-col sm:flex-row flex-wrap items-center justify-center gap-4">
              <a
                href={`${getWhatsAppChatUrl()}?text=${encodeURIComponent(t('whatsapp.greeting'))}`}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-2.5 px-6 py-3.5 border border-transparent bg-whatsapp text-white text-base font-semibold rounded-full hover:bg-whatsapp-dark transition-all shadow-lg shadow-green-200 dark:shadow-green-900/30 hover:shadow-xl hover:shadow-green-300 dark:hover:shadow-green-900/40 hover:-translate-y-0.5 active:translate-y-0 whitespace-nowrap"
              >
                <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 24 24">
                  <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413z" />
                </svg>
                {t('hero.cta')}
              </a>
              <a
                href="#services"
                className="inline-flex items-center gap-2 px-6 py-3.5 bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-200 text-base font-semibold rounded-full border border-gray-200 dark:border-gray-700 hover:border-brand-300 dark:hover:border-brand-500 hover:text-brand-700 dark:hover:text-brand-400 hover:shadow-md transition-all whitespace-nowrap"
              >
                {t('hero.secondaryCta')}
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                </svg>
              </a>
              </div>
              {/* Phone CTA — centered on its own row */}
              <div className="flex justify-center">
              <a
                href="tel:+358468044231"
                className="inline-flex items-center gap-2 px-6 py-3.5 bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-200 text-base font-semibold rounded-full border border-gray-200 dark:border-gray-700 hover:border-brand-300 dark:hover:border-brand-500 hover:text-brand-700 dark:hover:text-brand-400 hover:shadow-md transition-all whitespace-nowrap"
              >
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 5a2 2 0 012-2h3.28a1 1 0 01.948.684l1.498 4.493a1 1 0 01-.502 1.21l-2.257 1.13a11.042 11.042 0 005.516 5.516l1.13-2.257a1 1 0 011.21-.502l4.493 1.498a1 1 0 01.684.949V19a2 2 0 01-2 2h-1C9.716 21 3 14.284 3 6V5z" />
                </svg>
                {t('hero.callUs')}
              </a>
              </div>
            </motion.div>

            {/* Trust badges — plain wrapper: a variant fade here would mask the
                StaggerGroup's own entrance (its IO fires at mount, above the fold). */}
            <div data-motion-reveal className="mt-10">
              <TrustBadges />
            </div>
          </div>

          {/* Visual column */}
          <motion.div
            className="flex-1 hidden lg:flex items-center justify-center"
            data-motion-reveal
            initial={{ opacity: 0, scale: 0.94, y: 24 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            transition={{ duration: 0.7, ease: 'easeOut', delay: 0.45 }}
          >
            <motion.div
              animate={isDesktop ? { y: [0, -10, 0] } : undefined}
              transition={{ duration: 5, repeat: Infinity, ease: 'easeInOut' }}
            >
            <div className="relative w-80 h-80 lg:w-96 lg:h-96">
              <div className="absolute inset-0 bg-gradient-to-br from-brand-400 to-brand-600 rounded-3xl rotate-6 opacity-20 dark:opacity-15" />
              <div className="absolute inset-0 bg-white dark:bg-gray-800 rounded-3xl shadow-2xl dark:shadow-gray-950/50 overflow-hidden">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={baseUrl('/images/hero.webp')}
                  alt={t('hero.headline')}
                  className="w-full h-full object-cover"
                />
                <div className="absolute inset-0 bg-gradient-to-t from-gray-900/70 via-gray-900/10 to-transparent" />
                <div className="absolute bottom-4 left-4 right-4 space-y-2">
                  <div className="flex items-center gap-2.5 bg-white/90 dark:bg-gray-900/90 backdrop-blur rounded-xl px-3 py-2">
                    <div className="w-6 h-6 rounded-full bg-brand-500 flex items-center justify-center shrink-0">
                      <svg className="w-3.5 h-3.5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                      </svg>
                    </div>
                    <span className="text-xs font-semibold text-gray-800 dark:text-gray-100">{t('services.home.title')}</span>
                  </div>
                  <div className="flex items-center gap-2.5 bg-white/90 dark:bg-gray-900/90 backdrop-blur rounded-xl px-3 py-2">
                    <div className="w-6 h-6 rounded-full bg-accent-500 flex items-center justify-center shrink-0">
                      <svg className="w-3.5 h-3.5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6" />
                      </svg>
                    </div>
                    <span className="text-xs font-semibold text-gray-800 dark:text-gray-100">{t('services.deep.title')}</span>
                  </div>
                  <div className="flex items-center gap-2.5 bg-white/90 dark:bg-gray-900/90 backdrop-blur rounded-xl px-3 py-2">
                    <div className="w-6 h-6 rounded-full bg-brand-600 flex items-center justify-center shrink-0">
                      <svg className="w-3.5 h-3.5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                      </svg>
                    </div>
                    <span className="text-xs font-semibold text-gray-800 dark:text-gray-100">{t('services.moveOut.title')}</span>
                  </div>
                </div>
              </div>
            </div>
            </motion.div>
          </motion.div>
        </motion.div>
      </div>

      {/* Wave divider */}
      <div className="absolute bottom-0 left-0 right-0">
        <svg viewBox="0 0 1440 100" className="w-full h-auto" preserveAspectRatio="none">
          <path
            d="M0,40 C360,80 720,0 1080,30 C1260,45 1380,55 1440,60 L1440,100 L0,100 Z"
            className="fill-white dark:fill-gray-950"
          />
        </svg>
      </div>
    </section>
  );
}
