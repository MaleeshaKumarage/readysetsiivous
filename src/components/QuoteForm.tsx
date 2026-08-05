'use client';

import { useState, type FormEvent } from 'react';
import { useLanguage } from '@/hooks/useLanguage';
import { buildWhatsAppMessage, openWhatsApp } from '@/lib/whatsapp';

type ServiceKey = 'homeCleaning' | 'deepCleaning' | 'moveOutCleaning' | 'officeCleaning';
type SizeKey =
  | 'sizeStudio'
  | 'sizeSmall'
  | 'sizeMedium'
  | 'sizeLarge'
  | 'sizeXLarge'
  | 'sizeRooms1'
  | 'sizeRooms3'
  | 'sizeRooms5'
  | 'sizeRooms7';

interface FormErrors {
  service?: string;
  size?: string;
  city?: string;
  date?: string;
}

export default function QuoteForm() {
  const { t, lang } = useLanguage();

  const [service, setService] = useState('');
  const [size, setSize] = useState('');
  const [city, setCity] = useState('');
  const [date, setDate] = useState('');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitting, setSubmitting] = useState(false);

  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  const minDate = tomorrow.toISOString().split('T')[0];

  const serviceKeys: ServiceKey[] = ['homeCleaning', 'deepCleaning', 'moveOutCleaning', 'officeCleaning'];
  const sizeKeys: SizeKey[] = [
    'sizeStudio', 'sizeSmall', 'sizeMedium', 'sizeLarge', 'sizeXLarge',
  ];

  function validate(): boolean {
    const e: FormErrors = {};
    if (!service) e.service = t('quoteForm.errorService');
    if (!city.trim()) e.city = t('quoteForm.errorCity');
    if (!date) e.date = t('quoteForm.errorDate');
    setErrors(e);
    return Object.keys(e).length === 0;
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!validate()) return;

    setSubmitting(true);

    const serviceName = t(`quoteForm.${service}`);
    const sizeName = t(`quoteForm.${size}`) || size;

    const message = buildWhatsAppMessage(
      {
        service: serviceName,
        size: sizeName,
        city: city.trim(),
        date,
      },
      {
        greeting: t('whatsapp.greeting'),
        serviceLabel: t('whatsapp.serviceLabel'),
        sizeLabel: t('whatsapp.sizeLabel'),
        cityLabel: t('whatsapp.cityLabel'),
        dateLabel: t('whatsapp.dateLabel'),
        languageNote: t('whatsapp.languageNote'),
      }
    );

    openWhatsApp(message);
    setTimeout(() => setSubmitting(false), 1500);
  }

  const inputClass =
    'w-full px-4 py-3 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-xl text-gray-900 dark:text-gray-100 text-sm font-medium focus:border-brand-400 dark:focus:border-brand-500 focus:ring-4 focus:ring-brand-50 dark:focus:ring-brand-500/10 outline-none transition-all appearance-none';
  const labelClass = 'block text-sm font-semibold text-gray-700 dark:text-gray-200 mb-1.5';

  return (
    <section id="quote" className="bg-white dark:bg-gray-950 section-padding">
      <div className="container-page">
        <div className="max-w-2xl mx-auto">
          <div className="text-center mb-8">
            <h2 className="heading-lg text-gray-900 dark:text-gray-50">{t('quoteForm.title')}</h2>
            <p className="mt-3 text-gray-500 dark:text-gray-400">{t('quoteForm.subtitle')}</p>
          </div>

          <form
            onSubmit={handleSubmit}
            noValidate
            className="bg-white dark:bg-gray-900 border border-gray-100 dark:border-gray-800 rounded-2xl shadow-xl shadow-gray-100 dark:shadow-gray-950/50 p-6 sm:p-8 space-y-5"
          >
            {/* Service Type */}
            <div>
              <label htmlFor="service" className={labelClass}>
                {t('quoteForm.serviceType')}
              </label>
              <select
                id="service"
                value={service}
                onChange={(e) => { setService(e.target.value); setErrors((p) => ({ ...p, service: undefined })); }}
                className={inputClass}
              >
                <option value="">{t('quoteForm.selectService')}</option>
                {serviceKeys.map((k) => (
                  <option key={k} value={k}>
                    {t(`quoteForm.${k}`)}
                  </option>
                ))}
              </select>
              {errors.service && (
                <p className="mt-1 text-xs text-red-500">{errors.service}</p>
              )}
            </div>

            {/* Property Size */}
            <div>
              <label htmlFor="size" className={labelClass}>
                {t('quoteForm.propertySize')}
              </label>
              <select
                id="size"
                value={size}
                onChange={(e) => setSize(e.target.value)}
                className={inputClass}
              >
                <option value="">{t('quoteForm.selectService')}</option>
                {sizeKeys.map((k) => (
                  <option key={k} value={k}>
                    {t(`quoteForm.${k}`)}
                  </option>
                ))}
              </select>
            </div>

            {/* City */}
            <div>
              <label htmlFor="city" className={labelClass}>
                {t('quoteForm.city')}
              </label>
              <input
                id="city"
                type="text"
                value={city}
                onChange={(e) => { setCity(e.target.value); setErrors((p) => ({ ...p, city: undefined })); }}
                placeholder={t('quoteForm.cityPlaceholder')}
                className={inputClass}
              />
              {errors.city && (
                <p className="mt-1 text-xs text-red-500">{errors.city}</p>
              )}
            </div>

            {/* Date */}
            <div>
              <label htmlFor="date" className={labelClass}>
                {t('quoteForm.date')}
              </label>
              <input
                id="date"
                type="date"
                value={date}
                min={minDate}
                onChange={(e) => { setDate(e.target.value); setErrors((p) => ({ ...p, date: undefined })); }}
                className={inputClass}
              />
              {errors.date && (
                <p className="mt-1 text-xs text-red-500">{errors.date}</p>
              )}
            </div>

            {/* Submit */}
            <button
              type="submit"
              disabled={submitting}
              className="w-full flex items-center justify-center gap-2.5 px-6 py-3.5 bg-whatsapp text-white text-base font-semibold rounded-full hover:bg-whatsapp-dark transition-all shadow-lg shadow-green-200 dark:shadow-green-900/30 hover:shadow-xl hover:shadow-green-300 dark:hover:shadow-green-900/40 hover:-translate-y-0.5 active:translate-y-0 disabled:opacity-60 disabled:cursor-not-allowed disabled:hover:translate-y-0"
            >
              {submitting ? (
                <>
                  <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                  </svg>
                  {t('quoteForm.submitting')}
                </>
              ) : (
                <>
                  <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 24 24">
                    <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413z" />
                  </svg>
                  {t('quoteForm.submit')}
                </>
              )}
            </button>
          </form>
        </div>
      </div>
    </section>
  );
}
