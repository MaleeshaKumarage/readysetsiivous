'use client';

import { useLanguage } from '@/hooks/useLanguage';

const POINTS = [
  {
    key: 'insurance',
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
    ),
  },
  {
    key: 'tracking',
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
    ),
  },
  {
    key: 'handover',
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 7h12m0 0l-4-4m4 4l-4 4m0 6H4m0 0l4 4m-4-4l4-4" />
    ),
  },
  {
    key: 'certified',
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
    ),
  },
];

export default function KeySecurity() {
  const { t } = useLanguage();

  return (
    <section className="bg-white dark:bg-gray-950 section-padding">
      <div className="container-page">
        <div className="max-w-4xl mx-auto">
          <div className="text-center mb-10">
            <h2 className="heading-lg text-gray-900 dark:text-gray-50">
              {t('keySecurity.title')}
            </h2>
            <p className="mt-3 text-gray-500 dark:text-gray-400">
              {t('keySecurity.subtitle')}
            </p>
          </div>

          <div className="grid sm:grid-cols-2 gap-5">
            {POINTS.map(({ key, icon }) => (
              <div
                key={key}
                className="flex gap-4 p-5 bg-gray-50 dark:bg-gray-800 rounded-xl border border-gray-100 dark:border-gray-700"
              >
                <div className="w-10 h-10 rounded-lg bg-brand-50 dark:bg-brand-500/10 flex items-center justify-center shrink-0">
                  <svg className="w-5 h-5 text-brand-600 dark:text-brand-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    {icon}
                  </svg>
                </div>
                <div>
                  <h4 className="text-sm font-bold text-gray-900 dark:text-gray-100">
                    {t(`keySecurity.${key}`)}
                  </h4>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
