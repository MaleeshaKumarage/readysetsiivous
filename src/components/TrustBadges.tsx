'use client';

import { useLanguage } from '@/hooks/useLanguage';
import { StaggerGroup, StaggerItem } from './motion';

const BADGE_ICONS = [
  <svg key="shield" className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
  </svg>,
  <svg key="user" className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m-7.712.962A4.002 4.002 0 0112 7a4 4 0 013.288 6.038M15 21H3v-2a6 6 0 0112 0v2z" />
  </svg>,
  <svg key="star" className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
  </svg>,
  <svg key="leaf" className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
  </svg>,
];

const badgeKeys = [
  'trustBadges.insured',
  'trustBadges.vetted',
  'trustBadges.satisfaction',
  'trustBadges.eco',
];

export default function TrustBadges() {
  const { t } = useLanguage();

  // Used only inside Hero (above the fold), so its whileInView fires at mount;
  // delay keeps the stagger in sync with the hero's own entrance (~0.45s).
  return (
    <StaggerGroup className="flex flex-wrap items-center justify-center gap-3 sm:gap-6" stagger={0.1} delay={0.45}>
      {badgeKeys.map((key, i) => (
        <StaggerItem
          key={key}
          className="flex items-center gap-2 px-3 py-1.5 sm:px-4 sm:py-2 rounded-full bg-white/60 dark:bg-gray-800/60 backdrop-blur-sm border border-white/50 dark:border-gray-700/50 text-xs sm:text-sm font-medium text-gray-700 dark:text-gray-200 shadow-sm"
        >
          <span className="text-brand-500 dark:text-brand-400 shrink-0">{BADGE_ICONS[i]}</span>
          <span className="whitespace-nowrap">{t(key)}</span>
        </StaggerItem>
      ))}
    </StaggerGroup>
  );
}
