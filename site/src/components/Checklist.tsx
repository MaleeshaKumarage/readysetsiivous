'use client';

import { useLanguage } from '@/hooks/useLanguage';

const CHECKLIST_ITEMS: { key: string; standard: boolean; deep: boolean }[] = [
  { key: 'dusting', standard: true, deep: true },
  { key: 'vacuuming', standard: true, deep: true },
  { key: 'mopping', standard: true, deep: true },
  { key: 'kitchenSurfaces', standard: true, deep: true },
  { key: 'bathroom', standard: true, deep: true },
  { key: 'trash', standard: true, deep: true },
  { key: 'mirrors', standard: true, deep: true },
  { key: 'cabinets', standard: false, deep: true },
  { key: 'baseboards', standard: false, deep: true },
  { key: 'appliances', standard: false, deep: true },
  { key: 'oven', standard: false, deep: true },
  { key: 'fridge', standard: false, deep: true },
  { key: 'windows', standard: false, deep: true },
  { key: 'fixtures', standard: false, deep: true },
  { key: 'walls', standard: false, deep: true },
];

export default function Checklist() {
  const { t } = useLanguage();

  return (
    <section id="checklist" className="bg-white dark:bg-gray-950 section-padding">
      <div className="container-page">
        <div className="text-center max-w-2xl mx-auto">
          <h2 className="heading-lg text-gray-900 dark:text-gray-50">{t('checklist.title')}</h2>
          <p className="mt-3 text-gray-500 dark:text-gray-400 text-lg">{t('checklist.subtitle')}</p>
        </div>

        {/* Desktop table */}
        <div className="mt-12 hidden sm:block max-w-3xl mx-auto">
          <div className="overflow-hidden rounded-2xl border border-gray-100 dark:border-gray-800 shadow-sm dark:shadow-gray-950/30">
            <table className="w-full">
              <thead>
                <tr className="bg-gray-50 dark:bg-gray-800">
                  <th className="text-left px-6 py-4 text-sm font-bold text-gray-700 dark:text-gray-200">
                    {t('checklist.items.dusting')}
                  </th>
                  <th className="text-left px-6 py-4 text-sm font-bold text-gray-700 dark:text-gray-200">
                    {t('checklist.standard')}
                  </th>
                  <th className="text-left px-6 py-4 text-sm font-bold text-gray-700 dark:text-gray-200">
                    {t('checklist.deep')}
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-50 dark:divide-gray-800">
                {CHECKLIST_ITEMS.map(({ key, standard, deep }) => (
                  <tr key={key} className="hover:bg-gray-50/50 dark:hover:bg-gray-800/50 transition-colors">
                    <td className="px-6 py-3.5 text-sm text-gray-700 dark:text-gray-300">{t(`checklist.items.${key}`)}</td>
                    <td className="px-6 py-3.5">
                      {standard ? (
                        <span className="inline-flex items-center gap-1 text-brand-600 dark:text-brand-400 text-sm font-semibold">
                          <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clipRule="evenodd" />
                          </svg>
                          {t('checklist.included')}
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 text-gray-300 dark:text-gray-600 text-sm">
                          <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clipRule="evenodd" />
                          </svg>
                          {t('checklist.notIncluded')}
                        </span>
                      )}
                    </td>
                    <td className="px-6 py-3.5">
                      {deep ? (
                        <span className="inline-flex items-center gap-1 text-brand-600 dark:text-brand-400 text-sm font-semibold">
                          <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clipRule="evenodd" />
                          </svg>
                          {t('checklist.included')}
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 text-gray-300 dark:text-gray-600 text-sm">
                          <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clipRule="evenodd" />
                          </svg>
                          {t('checklist.notIncluded')}
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Mobile cards */}
        <div className="mt-10 sm:hidden space-y-8">
          <div className="rounded-2xl border border-gray-100 dark:border-gray-800 shadow-sm overflow-hidden">
            <div className="bg-brand-500 text-white px-5 py-3 text-center">
              <span className="font-bold text-sm">{t('checklist.standard')}</span>
            </div>
            <div className="divide-y divide-gray-50 dark:divide-gray-800 px-5 py-3">
              {CHECKLIST_ITEMS.map(({ key, standard }) => (
                <div key={key} className="flex items-center gap-3 py-2.5">
                  {standard ? (
                    <svg className="w-4 h-4 text-brand-500 dark:text-brand-400 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                      <path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clipRule="evenodd" />
                    </svg>
                  ) : (
                    <svg className="w-4 h-4 text-gray-300 dark:text-gray-600 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                      <path fillRule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clipRule="evenodd" />
                    </svg>
                  )}
                  <span className={`text-sm ${standard ? 'text-gray-700 dark:text-gray-300' : 'text-gray-300 dark:text-gray-600 line-through'}`}>
                    {t(`checklist.items.${key}`)}
                  </span>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded-2xl border border-gray-100 dark:border-gray-800 shadow-sm overflow-hidden">
            <div className="bg-accent-500 text-white px-5 py-3 text-center">
              <span className="font-bold text-sm">{t('checklist.deep')}</span>
            </div>
            <div className="divide-y divide-gray-50 dark:divide-gray-800 px-5 py-3">
              {CHECKLIST_ITEMS.map(({ key, deep }) => (
                <div key={key} className="flex items-center gap-3 py-2.5">
                  {deep ? (
                    <svg className="w-4 h-4 text-accent-500 dark:text-accent-400 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                      <path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clipRule="evenodd" />
                    </svg>
                  ) : (
                    <svg className="w-4 h-4 text-gray-300 dark:text-gray-600 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                      <path fillRule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clipRule="evenodd" />
                    </svg>
                  )}
                  <span className={`text-sm ${deep ? 'text-gray-700 dark:text-gray-300' : 'text-gray-300 dark:text-gray-600 line-through'}`}>
                    {t(`checklist.items.${key}`)}
                  </span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
