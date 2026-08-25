'use client';

import { useLanguage } from '@/hooks/useLanguage';
import { Reveal, StaggerGroup, StaggerItem } from './motion';

function Stars() {
  return (
    <div className="flex gap-0.5 text-brand-500" aria-label="5 out of 5 stars" role="img">
      {[0, 1, 2, 3, 4].map((i) => (
        <svg key={i} className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
        </svg>
      ))}
    </div>
  );
}

export default function Testimonials() {
  const { t } = useLanguage();

  return (
    <section id="reviews" className="bg-white dark:bg-gray-950 section-padding">
      <div className="container-page">
        <Reveal className="text-center max-w-2xl mx-auto">
          <h2 className="heading-lg text-gray-900 dark:text-gray-50">{t('testimonials.title')}</h2>
          <p className="mt-3 text-gray-500 dark:text-gray-400 text-lg">{t('testimonials.subtitle')}</p>
        </Reveal>

        <StaggerGroup className="mt-10 grid sm:grid-cols-2 lg:grid-cols-3 gap-6">
          {[0, 1, 2].map((i) => (
            <StaggerItem
              key={i}
              className="bg-gray-50 dark:bg-gray-900 border border-gray-100 dark:border-gray-800 rounded-2xl p-6 flex flex-col gap-3"
            >
              <Stars />
              <p className="text-sm text-gray-600 dark:text-gray-300 leading-relaxed flex-1">
                “{t(`testimonials.items.${i}.text`)}”
              </p>
              <div className="flex items-center gap-3 pt-1">
                <div className="w-9 h-9 rounded-full bg-accent-500 flex items-center justify-center text-white text-sm font-bold shrink-0">
                  {t(`testimonials.items.${i}.name`).charAt(0)}
                </div>
                <div>
                  <p className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {t(`testimonials.items.${i}.name`)}
                  </p>
                  <p className="text-xs text-gray-500 dark:text-gray-400">
                    {t(`testimonials.items.${i}.area`)}
                  </p>
                </div>
              </div>
            </StaggerItem>
          ))}
        </StaggerGroup>
      </div>
    </section>
  );
}
