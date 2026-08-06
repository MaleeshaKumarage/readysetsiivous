'use client';

import { useLanguage } from '@/hooks/useLanguage';

const BADGES = [
  { key: 'ecoTitle', desc: 'ecoDesc', icon: '🌱', color: 'bg-green-50 dark:bg-green-500/10 text-green-700 dark:text-green-400 border-green-200 dark:border-green-500/20' },
  { key: 'allergyTitle', desc: 'allergyDesc', icon: '🌸', color: 'bg-pink-50 dark:bg-pink-500/10 text-pink-700 dark:text-pink-400 border-pink-200 dark:border-pink-500/20' },
  { key: 'petTitle', desc: 'petDesc', icon: '🐾', color: 'bg-amber-50 dark:bg-amber-500/10 text-amber-700 dark:text-amber-400 border-amber-200 dark:border-amber-500/20' },
  { key: 'certifiedTitle', desc: 'certifiedDesc', icon: '🇪🇺', color: 'bg-blue-50 dark:bg-blue-500/10 text-blue-700 dark:text-blue-400 border-blue-200 dark:border-blue-500/20' },
];

export default function EcoPetBadges() {
  const { t } = useLanguage();

  return (
    <section className="bg-gray-50 dark:bg-gray-900 section-padding">
      <div className="container-page">
        <div className="text-center mb-10">
          <h2 className="heading-lg text-gray-900 dark:text-gray-50">
            {t('ecoPetBadges.title')}
          </h2>
        </div>

        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 max-w-4xl mx-auto">
          {BADGES.map(({ key, desc, icon, color }) => (
            <div
              key={key}
              className={`rounded-xl border p-5 text-center ${color} transition-all hover:shadow-md`}
            >
              <span className="text-3xl block mb-2">{icon}</span>
              <h4 className="text-sm font-bold">{t(`ecoPetBadges.${key}`)}</h4>
              <p className="mt-1 text-xs opacity-80">{t(`ecoPetBadges.${desc}`)}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
