'use client';

import { useEffect, useState } from 'react';
import { useLanguage } from '@/hooks/useLanguage';
import { baseUrl } from '@/lib/whatsapp';
import { API_URL, fetchServices, type PublicService } from '@/lib/api';
import { serviceIcon } from '@/lib/icons';
// Entrance animation removed from this section: cards must stay visible even
// when JS chunks are stale (Pages deploy replaces _next assets).

// i18n key → API slug mapping. Static export keeps the hardcoded cards as
// fallback when the API is unreachable; API data wins when it answers.
const KEY_TO_SLUG: Record<string, string> = {
  'services.home': 'home-cleaning',
  'services.office': 'office-cleaning',
  'services.moveOut': 'move-out-cleaning',
  'services.deep': '',
};

const SERVICE_CARDS = [
  {
    key: 'services.home',
    image: 'service-home.webp',
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6" />
    ),
    gradient: 'from-brand-500 to-brand-600',
    bg: 'bg-brand-50 dark:bg-brand-500/10',
  },
  {
    key: 'services.deep',
    image: 'service-deep.webp',
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z" />
    ),
    gradient: 'from-accent-500 to-accent-600',
    bg: 'bg-accent-50 dark:bg-accent-500/10',
  },
  {
    key: 'services.moveOut',
    image: 'service-moveout.webp',
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
    ),
    gradient: 'from-violet-500 to-purple-600',
    bg: 'bg-violet-50 dark:bg-violet-500/10',
  },
  {
    key: 'services.office',
    image: 'service-office.webp',
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
    ),
    gradient: 'from-amber-500 to-orange-600',
    bg: 'bg-amber-50 dark:bg-amber-500/10',
  },
];

export default function Services() {
  const { t, lang } = useLanguage();
  const [apiServices, setApiServices] = useState<PublicService[]>([]);

  useEffect(() => {
    fetchServices(lang).then((services) => {
      if (services && services.length > 0) {
        // First four active services, admin-controlled order.
        setApiServices([...services].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0)).slice(0, 4));
      }
    });
  }, [lang]);

  const GRADIENTS = ['from-brand-500 to-brand-600', 'from-accent-500 to-accent-600', 'from-violet-500 to-purple-600', 'from-amber-500 to-orange-600'];
  const BGS = ['bg-brand-50 dark:bg-brand-500/10', 'bg-accent-50 dark:bg-accent-500/10', 'bg-violet-50 dark:bg-violet-500/10', 'bg-amber-50 dark:bg-amber-500/10'];
  const FALLBACK_IMAGES: Record<string, string> = {
    'home-cleaning': 'service-home.webp',
    'deep-cleaning': 'service-deep.webp',
    'move-out-cleaning': 'service-moveout.webp',
    'office-cleaning': 'service-office.webp',
  };

  return (
    <section id="services" className="bg-gray-50 dark:bg-gray-900 section-padding">
      <div className="container-page">
        <div className="text-center max-w-2xl mx-auto">
          <h2 className="heading-lg text-gray-900 dark:text-gray-50">{t('services.title')}</h2>
          <p className="mt-3 text-gray-500 dark:text-gray-400 text-lg">{t('services.subtitle')}</p>
        </div>

        <div className="mt-12 grid sm:grid-cols-2 lg:grid-cols-4 gap-6">
          {apiServices.length > 0
            ? apiServices.map((s, i) => {
                const IconCmp = serviceIcon(s.icon);
                const gradient = GRADIENTS[i % GRADIENTS.length];
                const bg = BGS[i % BGS.length];
                return (
                  <div
                    key={s.id}
                    className="group relative bg-white dark:bg-gray-800 rounded-2xl border border-gray-100 dark:border-gray-700 shadow-sm dark:shadow-gray-950/30 hover:shadow-xl dark:hover:shadow-gray-950/50 p-6 flex flex-col"
                  >
                    <div className="h-36 -mx-6 -mt-6 mb-4 overflow-hidden rounded-t-2xl bg-gray-100 dark:bg-gray-700">
                      {/* eslint-disable-next-line @next/next/no-img-element */}
                      <img
                        src={s.imageUrl ? `${API_URL}${s.imageUrl}` : baseUrl(`/images/${FALLBACK_IMAGES[s.slug] ?? 'service-home.webp'}`)}
                        alt={s.name}
                        className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-105"
                        loading="lazy"
                      />
                    </div>
                    <div className={`w-12 h-12 rounded-xl bg-gradient-to-br ${gradient} flex items-center justify-center shrink-0`}>
                      <IconCmp className="w-6 h-6 text-white" />
                    </div>
                    <h3 className="mt-4 text-lg font-bold text-gray-900 dark:text-gray-100">{s.name}</h3>
                    <p className="mt-2 text-sm text-gray-500 dark:text-gray-400 leading-relaxed flex-1">{s.description}</p>
                    {s.additionalInfo && (
                      <p className="mt-2 text-xs text-gray-400 dark:text-gray-500">{s.additionalInfo}</p>
                    )}
                    <p className="mt-4 text-sm font-bold text-brand-600 dark:text-brand-400">
                      {t('services.from')} {s.priceNet.toFixed(0)} € · {s.durationMinutes} min
                    </p>
                    <div className={`absolute -inset-0.5 rounded-2xl -z-10 opacity-0 group-hover:opacity-100 transition-opacity blur-sm ${bg}`} />
                  </div>
                );
              })
            : SERVICE_CARDS.map(({ key, image, icon, gradient, bg }) => (
                <div
                  key={key}
                  className="group relative bg-white dark:bg-gray-800 rounded-2xl border border-gray-100 dark:border-gray-700 shadow-sm dark:shadow-gray-950/30 hover:shadow-xl dark:hover:shadow-gray-950/50 p-6 flex flex-col"
                >
                  <div className="h-36 -mx-6 -mt-6 mb-4 overflow-hidden rounded-t-2xl">
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img
                      src={baseUrl(`/images/${image}`)}
                      alt={t(`${key}.title`)}
                      className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-105"
                      loading="lazy"
                    />
                  </div>
                  <div className={`w-12 h-12 rounded-xl bg-gradient-to-br ${gradient} flex items-center justify-center shrink-0`}>
                    <svg className="w-6 h-6 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      {icon}
                    </svg>
                  </div>
                  <h3 className="mt-4 text-lg font-bold text-gray-900 dark:text-gray-100">{t(`${key}.title`)}</h3>
                  <p className="mt-2 text-sm text-gray-500 dark:text-gray-400 leading-relaxed flex-1">{t(`${key}.description`)}</p>
                  <p className="mt-4 text-sm font-bold text-brand-600 dark:text-brand-400">{t(`${key}.price`)}</p>
                  <div className={`absolute -inset-0.5 rounded-2xl -z-10 opacity-0 group-hover:opacity-100 transition-opacity blur-sm ${bg}`} />
                </div>
              ))}
        </div>
      </div>
    </section>
  );
}
