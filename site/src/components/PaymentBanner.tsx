'use client';

import { useLanguage } from '@/hooks/useLanguage';
import { Reveal } from './motion';

export default function PaymentBanner() {
  const { t } = useLanguage();

  return (
    <section className="bg-white dark:bg-gray-950 border-t border-gray-100 dark:border-gray-800 py-10">
      <div className="container-page">
        <Reveal className="flex flex-col sm:flex-row items-center justify-center gap-4 sm:gap-8">
          <span className="text-sm font-semibold text-gray-500 dark:text-gray-400 uppercase tracking-wider">
            {t('paymentBanner.title')}
          </span>

          <div className="flex flex-wrap items-center justify-center gap-4 sm:gap-6">
            {/* MobilePay */}
            <div className="flex items-center gap-2 text-sm font-medium text-gray-700 dark:text-gray-300">
              <span className="w-8 h-8 rounded-lg bg-indigo-500 flex items-center justify-center text-white text-xs font-bold">
                MP
              </span>
              <span>{t('paymentBanner.mobilepay')}</span>
            </div>

            {/* Invoice */}
            <div className="flex items-center gap-2 text-sm font-medium text-gray-700 dark:text-gray-300">
              <svg className="w-5 h-5 text-gray-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
              <span>{t('paymentBanner.invoice')}</span>
            </div>

            {/* Cards */}
            <div className="flex items-center gap-2 text-sm font-medium text-gray-700 dark:text-gray-300">
              <svg className="w-5 h-5 text-gray-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z" />
              </svg>
              <span>{t('paymentBanner.card')}</span>
            </div>

            {/* Cash */}
            <div className="flex items-center gap-2 text-sm font-medium text-gray-700 dark:text-gray-300">
              <svg className="w-5 h-5 text-gray-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 9V7a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2m2 4h10a2 2 0 002-2v-6a2 2 0 00-2-2H9a2 2 0 00-2 2v6a2 2 0 002 2zm7-5a2 2 0 11-4 0 2 2 0 014 0z" />
              </svg>
              <span>{t('paymentBanner.cash')}</span>
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
