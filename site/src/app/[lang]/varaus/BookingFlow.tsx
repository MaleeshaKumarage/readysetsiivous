'use client';

import { useEffect, useMemo, useState } from 'react';
import {
  fetchServices,
  fetchAvailability,
  createBooking,
  type PublicService,
  type AvailabilitySlot,
} from '@/lib/api';
import { type Language, t } from '@/i18n';
import { openWhatsApp } from '@/lib/whatsapp';

type Step = 'service' | 'time' | 'details' | 'done';

export default function BookingFlow({ lang }: { lang: Language }) {
  const [services, setServices] = useState<PublicService[] | null>(null);
  const [apiError, setApiError] = useState(false);
  const [step, setStep] = useState<Step>('service');
  const [service, setService] = useState<PublicService | null>(null);
  const [date, setDate] = useState('');
  const [slots, setSlots] = useState<AvailabilitySlot[] | null>(null);
  const [slot, setSlot] = useState('');
  const [form, setForm] = useState({
    customerName: '',
    customerPhone: '',
    customerEmail: '',
    street: '',
    postalCode: '',
    city: '',
    notes: '',
  });
  const [result, setResult] = useState<{ bookingNumber: string; customerReference: string } | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(false);

  useEffect(() => {
    fetchServices(lang).then((data) => {
      setServices(data);
      if (data === null) setApiError(true);
    });
  }, [lang]);

  useEffect(() => {
    if (step === 'time' && service && date) {
      setSlots(null);
      fetchAvailability(date, service.id).then(setSlots);
    }
  }, [step, service, date]);

  const today = useMemo(() => new Date().toISOString().slice(0, 10), []);

  const update = (key: keyof typeof form) => (
    e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>
  ) => setForm((f) => ({ ...f, [key]: e.target.value }));

  async function submit() {
    if (!service || !slot || !date) return;
    setSubmitting(true);
    setSubmitError(false);
    const res = await createBooking({
      localDate: date,
      startTime: slot,
      serviceId: service.id,
      ...form,
      notes: form.notes || null,
    });
    setSubmitting(false);
    if (res) {
      setResult(res);
      setStep('done');
    } else {
      setSubmitError(true);
    }
  }

  if (apiError) {
    return (
      <div className="mx-auto max-w-2xl px-4 text-center">
        <h1 className="mb-4 text-2xl font-bold">{t(lang, 'varaus.unavailableTitle')}</h1>
        <p className="mb-6 text-gray-500 dark:text-gray-400">{t(lang, 'varaus.unavailableText')}</p>
        <button
          onClick={() => openWhatsApp(t(lang, 'varaus.whatsappMessage'))}
          className="rounded-full bg-brand px-6 py-3 font-semibold text-accent-950"
        >
          {t(lang, 'varaus.contactViaWhatsApp')}
        </button>
      </div>
    );
  }

  if (!services) {
    return <p className="text-center text-gray-500 dark:text-gray-400">{t(lang, 'varaus.loading')}</p>;
  }

  return (
    <div className="mx-auto max-w-2xl px-4">
      <h1 className="mb-8 text-3xl font-bold">{t(lang, 'varaus.title')}</h1>

      {step === 'service' && (
        <div className="grid gap-4">
          {services.map((s) => (
            <button
              key={s.id}
              onClick={() => {
                setService(s);
                setStep('time');
              }}
              className="rounded-xl border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 p-5 text-left transition hover:border-brand"
            >
              <div className="flex items-baseline justify-between gap-4">
                <span className="text-lg font-semibold">{s.name}</span>
                <span className="whitespace-nowrap text-brand">
                  {t(lang, 'varaus.from')} {s.priceNet.toFixed(2).replace('.', ',')} € / {t(lang, 'varaus.hour')}
                </span>
              </div>
              <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">{s.description}</p>
            </button>
          ))}
        </div>
      )}

      {step === 'time' && service && (
        <div>
          <button onClick={() => setStep('service')} className="mb-4 text-sm underline">
            ← {t(lang, 'varaus.back')}
          </button>
          <h2 className="mb-4 text-xl font-semibold">{service.name}</h2>
          <label className="mb-6 block">
            <span className="mb-1 block text-sm font-medium">{t(lang, 'varaus.date')}</span>
            <input
              type="date"
              min={today}
              value={date}
              onChange={(e) => setDate(e.target.value)}
              className="w-full rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2"
            />
          </label>
          {date && (
            <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
              {slots === null ? (
                <p className="col-span-full text-gray-500 dark:text-gray-400">{t(lang, 'varaus.loadingSlots')}</p>
              ) : slots.length === 0 ? (
                <p className="col-span-full text-gray-500 dark:text-gray-400">{t(lang, 'varaus.noSlots')}</p>
              ) : (
                slots.map((s) => (
                  <button
                    key={s.startTime}
                    onClick={() => {
                      setSlot(s.startTime);
                      setStep('details');
                    }}
                    className="rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2 text-sm hover:border-brand"
                  >
                    {s.startTime}
                  </button>
                ))
              )}
            </div>
          )}
        </div>
      )}

      {step === 'details' && service && (
        <div>
          <button onClick={() => setStep('time')} className="mb-4 text-sm underline">
            ← {t(lang, 'varaus.back')}
          </button>
          <h2 className="mb-1 text-xl font-semibold">{t(lang, 'varaus.detailsTitle')}</h2>
          <p className="mb-6 text-sm text-gray-500 dark:text-gray-400">
            {service.name} · {date} · {t(lang, 'varaus.at')} {slot}
          </p>
          <div className="grid gap-4">
            <Field label={t(lang, 'varaus.name')} value={form.customerName} onChange={update('customerName')} />
            <Field label={t(lang, 'varaus.phone')} value={form.customerPhone} onChange={update('customerPhone')} />
            <Field label={t(lang, 'varaus.email')} value={form.customerEmail} onChange={update('customerEmail')} />
            <Field label={t(lang, 'varaus.street')} value={form.street} onChange={update('street')} />
            <div className="grid grid-cols-2 gap-4">
              <Field label={t(lang, 'varaus.postalCode')} value={form.postalCode} onChange={update('postalCode')} />
              <Field label={t(lang, 'varaus.city')} value={form.city} onChange={update('city')} />
            </div>
            <label className="block">
              <span className="mb-1 block text-sm font-medium">{t(lang, 'varaus.notes')}</span>
              <textarea
                value={form.notes}
                onChange={update('notes')}
                rows={3}
                className="w-full rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2"
              />
            </label>
            {submitError && (
              <p className="text-sm text-red-500">{t(lang, 'varaus.submitError')}</p>
            )}
            <button
              onClick={submit}
              disabled={submitting || !form.customerName || !form.customerPhone || !form.street || !form.city}
              className="rounded-full bg-brand px-6 py-3 font-semibold text-accent-950 disabled:opacity-50"
            >
              {submitting ? t(lang, 'varaus.submitting') : t(lang, 'varaus.confirm')}
            </button>
          </div>
        </div>
      )}

      {step === 'done' && result && (
        <div className="text-center">
          <h2 className="mb-2 text-2xl font-bold">{t(lang, 'varaus.doneTitle')}</h2>
          <p className="mb-4 text-gray-500 dark:text-gray-400">
            {t(lang, 'varaus.bookingNumber')}: <strong>{result.bookingNumber}</strong>
          </p>
          <p className="mb-8 text-gray-500 dark:text-gray-400">
            {t(lang, 'varaus.reference')}: <strong>{result.customerReference}</strong>
          </p>
          <a href="/" className="rounded-full bg-brand px-6 py-3 font-semibold text-accent-950">
            {t(lang, 'varaus.backHome')}
          </a>
        </div>
      )}
    </div>
  );
}

function Field({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-sm font-medium">{label}</span>
      <input
        value={value}
        onChange={onChange}
        className="w-full rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-3 py-2"
      />
    </label>
  );
}
