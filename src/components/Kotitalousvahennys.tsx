'use client';

import { useLanguage } from '@/hooks/useLanguage';

const PRICES = [
  { key: 'services.home', list: 35, after: 14 },
  { key: 'services.deep', list: 45, after: 18 },
  { key: 'services.moveOut', list: 50, after: 20 },
  { key: 'services.office', list: 30, after: 12 },
];

export default function Kotitalousvahennys() {
  const { t } = useLanguage();

  return (
    <section className="bg-gradient-to-r from-brand-500 to-brand-600 dark:from-brand-600 dark:to-brand-700 section-padding">
      <div className="container-page">
        <div className="max-w-4xl mx-auto text-center">
          <span className="inline-block px-4 py-1.5 bg-white/20 text-white text-xs font-bold rounded-full uppercase tracking-wider mb-4">
            Suomi 🇫🇮
          </span>
          <h2 className="heading-lg text-white">{t('kotitalousvahennys.title')}</h2>
          <p className="mt-4 text-brand-50 text-lg leading-relaxed">
            {t('kotitalousvahennys.description')}
          </p>

          {/* Price calculator cards */}
          <div className="mt-8 grid grid-cols-2 sm:grid-cols-4 gap-3">
            {PRICES.map(({ key, list, after }) => (
              <div
                key={key}
                className="bg-white/10 backdrop-blur-sm rounded-xl p-4 border border-white/20 text-white"
              >
                <p className="text-xs text-brand-100 mb-2">{t(`${key}.title`)}</p>
                <div className="flex flex-col items-center gap-1">
                  <span className="text-sm line-through text-brand-200">{t(`${key}.price`)}</span>
                  <span className="text-xl font-extrabold">{t('kotitalousvahennys.after')}</span>
                  <span className="text-2xl font-black">~{after} €/h</span>
                </div>
              </div>
            ))}
          </div>

          <p className="mt-6 text-sm text-brand-100">{t('kotitalousvahennys.note')}</p>
          <a
            href="https://www.vero.fi/en/individuals/housing/tax-credit-for-household-expenses/"
            target="_blank"
            rel="noopener noreferrer"
            className="mt-4 inline-flex items-center gap-2 text-sm font-semibold text-white hover:text-brand-100 underline underline-offset-4"
          >
            {t('kotitalousvahennys.cta')}
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
            </svg>
          </a>
        </div>
      </div>
    </section>
  );
}
