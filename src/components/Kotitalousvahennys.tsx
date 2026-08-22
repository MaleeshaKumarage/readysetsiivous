'use client';

import { useLanguage } from '@/hooks/useLanguage';
import { Reveal, StaggerGroup, StaggerItem } from './motion';

const PRICES = [
  'services.home',
  'services.deep',
  'services.moveOut',
  'services.window',
  'services.ovenFridge',
  'services.saunaBalcony',
];

export default function Kotitalousvahennys() {
  const { t } = useLanguage();

  return (
    <section className="bg-white dark:bg-gray-950 section-padding">
      <div className="container-page">
        <div className="max-w-4xl mx-auto">
          {/* Header card */}
          <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-brand-500 to-brand-600 dark:from-brand-600 dark:to-brand-700 p-6 sm:p-8 text-center text-white shadow-lg shadow-brand-200/30 dark:shadow-brand-900/20">
            {/* Background pattern */}
            <div className="absolute top-0 right-0 w-32 h-32 bg-white/5 rounded-full -translate-y-1/2 translate-x-1/2 pointer-events-none" />
            <div className="absolute bottom-0 left-0 w-24 h-24 bg-white/5 rounded-full translate-y-1/2 -translate-x-1/2 pointer-events-none" />

            <span className="relative inline-flex items-center gap-1.5 px-3 py-1 bg-white/20 rounded-full text-xs font-bold uppercase tracking-wider mb-4">
              🇫🇮 Suomi
            </span>
            <h2 className="relative text-2xl sm:text-3xl font-extrabold">
              {t('kotitalousvahennys.title')}
            </h2>
            <p className="relative mt-3 text-brand-50 text-base sm:text-lg leading-relaxed max-w-2xl mx-auto">
              {t('kotitalousvahennys.description')}
            </p>
          </div>

          {/* Price cards */}
          <StaggerGroup className="mt-6 grid grid-cols-2 sm:grid-cols-3 gap-3 sm:gap-4">
            {PRICES.map((key) => (
              <StaggerItem
                key={key}
                hover
                className="group bg-gray-50 dark:bg-gray-800 rounded-xl border border-gray-100 dark:border-gray-700 p-5 text-center hover:shadow-md hover:border-brand-200 dark:hover:border-brand-500/30"
              >
                <p className="text-xs font-semibold text-gray-500 dark:text-gray-400 uppercase tracking-wider mb-3">
                  {t(`${key}.title`)}
                </p>
                <p className="text-xl font-extrabold text-brand-600 dark:text-brand-400">
                  {t(`${key}.price`)}
                </p>
              </StaggerItem>
            ))}
          </StaggerGroup>

          {/* Bottom note — two lines */}
          <div className="mt-6 flex flex-col items-center gap-2 text-center">
            <p className="text-sm text-gray-500 dark:text-gray-400 max-w-xl">
              {t('kotitalousvahennys.note')}
            </p>
            <a
              href="https://www.vero.fi/en/individuals/housing/tax-credit-for-household-expenses/"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1.5 text-sm font-semibold text-brand-600 dark:text-brand-400 hover:text-brand-700 dark:hover:text-brand-300 underline underline-offset-4 transition-colors"
            >
              {t('kotitalousvahennys.cta')}
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
              </svg>
            </a>
          </div>
        </div>
      </div>
    </section>
  );
}
