'use client';

import { useLanguage } from '@/hooks/useLanguage';
import { StaggerGroup, StaggerItem } from './motion';

const ITEMS = [
  { key: 'vat', icon: '€' },
  { key: 'supplies', icon: '🧴' },
  { key: 'travel', icon: '🚗' },
  { key: 'hidden', icon: '✅' },
];

export default function PricingTransparency() {
  const { t } = useLanguage();

  return (
    <section className="bg-gray-50 dark:bg-gray-900 section-padding">
      <div className="container-page">
        <div className="text-center max-w-2xl mx-auto">
          <h2 className="heading-lg text-gray-900 dark:text-gray-50">
            {t('pricingTransparency.title')}
          </h2>
          <p className="mt-3 text-gray-500 dark:text-gray-400">
            {t('pricingTransparency.subtitle')}
          </p>
        </div>

        <StaggerGroup className="mt-10 grid grid-cols-2 lg:grid-cols-4 gap-4 max-w-4xl mx-auto">
          {ITEMS.map(({ key, icon }) => (
            <StaggerItem
              key={key}
              hover
              className="bg-white dark:bg-gray-800 rounded-xl border border-gray-100 dark:border-gray-700 p-5 text-center shadow-sm hover:shadow-md"
            >
              <span className="text-2xl">{icon}</span>
              <p className="mt-2 text-sm font-semibold text-gray-700 dark:text-gray-200">
                {t(`pricingTransparency.${key}`)}
              </p>
            </StaggerItem>
          ))}
        </StaggerGroup>
      </div>
    </section>
  );
}
