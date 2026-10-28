'use client';

import { useEffect, useRef, useState, type FormEvent } from 'react';
import { fetchServices, fetchAvailability, publicPost, type BookingResult, type PublicService, type AvailabilitySlot, type PublicFailure } from '@/lib/api';
import { type Language, t } from '@/i18n';
import { publicUx } from '@/i18n/publicUx';
import { gross, money, fullDate, helsinkiToday, downloadConfirmation } from '@/lib/publicFormatting';
import { openWhatsApp, baseUrl } from '@/lib/whatsapp';

type Step = 'service' | 'time' | 'details' | 'done';
const inputClass = 'w-full rounded-lg border border-gray-400 dark:border-gray-500 bg-white dark:bg-gray-800 px-3 py-3';
const buttonClass = 'rounded-full bg-brand-500 px-6 py-3 font-semibold text-accent-950 disabled:opacity-60';

export default function BookingFlow({ lang }: { lang: Language }) {
  const u = publicUx(lang);
  const [services, setServices] = useState<PublicService[] | null>(null);
  const [apiError, setApiError] = useState(false);
  const [step, setStep] = useState<Step>('service');
  const [service, setService] = useState<PublicService | null>(null);
  const [date, setDate] = useState('');
  const [slots, setSlots] = useState<AvailabilitySlot[] | null>(null);
  const [slotsError, setSlotsError] = useState(false);
  const [slotsRetry, setSlotsRetry] = useState(0);
  const [slot, setSlot] = useState('');
  const [form, setForm] = useState({ customerName: '', customerPhone: '', customerEmail: '', street: '', postalCode: '', city: '', notes: '' });
  const [result, setResult] = useState<BookingResult | null>(null);
  const [copyState, setCopyState] = useState<'idle' | 'success' | 'failure'>('idle');
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState<PublicFailure | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [accepted, setAccepted] = useState(false);
  const [minimum, setMinimum] = useState('');
  const heading = useRef<HTMLHeadingElement>(null);
  const busy = useRef(false);
  useEffect(() => { setMinimum(helsinkiToday()); const timer = setInterval(() => setMinimum(helsinkiToday()), 60000); return () => clearInterval(timer); }, []);
  useEffect(() => { let alive = true; fetchServices(lang).then(data => { if (alive) { setServices(data); setApiError(data === null); } }); return () => { alive = false; }; }, [lang]);
  useEffect(() => { heading.current?.focus(); }, [step]);
  useEffect(() => {
    if (!submitting && failure === 'validation') document.getElementById(Object.keys(errors)[0])?.focus();
  }, [submitting, failure, errors]);
  useEffect(() => {
    let cancelled = false;
    if (step === 'time' && service && date) {
      setSlots(null); setSlotsError(false);
      fetchAvailability(date, service.id).then(data => { if (!cancelled) { setSlots(data); setSlotsError(data === null); } });
    }
    return () => { cancelled = true; };
  }, [step, service, date, slotsRetry]);
  function update(key: keyof typeof form, value: string) { setForm(f => ({ ...f, [key]: value })); setErrors(e => ({ ...e, [key]: '' })); }
  async function submit(event: FormEvent) {
    event.preventDefault(); if (busy.current || failure === 'uncertain' || !service || !slot || !date) return;
    const next: Record<string, string> = {};
    for (const key of ['customerName', 'customerPhone', 'street', 'city'] as const) if (!form[key].trim()) next[key] = u.invalid;
    if (form.customerEmail && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.customerEmail)) next.customerEmail = u.emailInvalid;
    if (!accepted) next.accepted = u.invalid;
    if (date < helsinkiToday()) { setFailure('validation'); setStep('time'); return; }
    setErrors(next);
    if (Object.keys(next).length) { setFailure('validation'); document.getElementById(Object.keys(next)[0])?.focus(); return; }
    busy.current = true; setSubmitting(true); setFailure(null);
    const response = await publicPost<BookingResult>('/bookings', { localDate: date, startTime: slot, serviceId: service.id, ...Object.fromEntries(Object.entries(form).map(([k,v]) => [k,v.trim()])), notes: form.notes.trim() || null, expectedPriceGross: gross(service) });
    busy.current = false; setSubmitting(false);
    if (response.error) {
      setFailure(response.error);
      const serverFields: Record<string,string> = {};
      for (const field of response.fields ?? []) { const key = field.charAt(0).toLowerCase() + field.slice(1); if (key in form) serverFields[key] = u.invalid; }
      setErrors(serverFields);
      if (response.error === 'conflict') { setSlot(''); setSlotsRetry(v => v + 1); setStep('time'); }
      else if (response.error === 'priceChanged') { setServices(null); setStep('service'); const fresh = await fetchServices(lang); setServices(fresh); setApiError(fresh === null); }
      else if (Object.keys(serverFields).length) document.getElementById(Object.keys(serverFields)[0])?.focus();
    } else if (!response.value?.bookingNumber || !response.value?.managementKey) {
      setFailure('uncertain');
    } else { setResult(response.value); setStep('done'); }
  }
  const summary = service && date && slot ? `${service.name} · ${fullDate(date, lang)} · ${slot} · ${money(result?.priceGross ?? gross(service), lang, result?.currency ?? service.currency ?? 'EUR')}` : '';
  const progress = ['service', 'time', 'details'].indexOf(step);
  return <div className="mx-auto max-w-2xl px-4">
    <h1 ref={heading} tabIndex={-1} aria-describedby={step === 'done' ? 'booking-result-status' : 'booking-current-step'} className="mb-6 text-3xl font-bold">{t(lang, 'varaus.title')}</h1>
    <a href={baseUrl(`/${lang}/booking/`)} className="underline">{u.manage}</a>
    {step !== 'done' && <ol aria-label={t(lang, 'varaus.title')} className="my-6 flex flex-wrap gap-4">{u.steps.map((name,i) => <li key={name} id={i === progress ? 'booking-current-step' : undefined} aria-current={i === progress ? 'step' : undefined} className={i === progress ? 'font-bold underline' : ''}>{i + 1}. {name}</li>)}</ol>}
    <p className="my-4 text-sm">{u.zone}</p>
    {failure && <p role="alert" className="my-4 rounded-lg border border-red-600 p-3 text-red-700 dark:text-red-300">{u[failure]}</p>}
    {(apiError || services?.length === 0) && <div><p>{apiError ? t(lang, 'varaus.unavailableText') : u.noServices}</p><button className={buttonClass} onClick={() => openWhatsApp(t(lang, 'varaus.whatsappMessage'))}>{t(lang, 'varaus.contactViaWhatsApp')}</button></div>}
    {!services && !apiError && <p role="status">{t(lang, 'varaus.loading')}</p>}
    {step === 'service' && services && <div className="grid gap-4">{services.map(s => <button key={s.id} className={`${inputClass} text-left`} onClick={() => { setService(s); setStep('time'); }}><strong className="block text-lg">{s.name}</strong><span className="block">{money(gross(s), lang, s.currency ?? 'EUR')} — {u.price}</span>{s.durationMinutes && <span>{u.duration}: {s.durationMinutes} {u.minutes}</span>}<span className="mt-2 block text-sm">{[s.description,s.additionalInfo].filter(Boolean).join(' ')}</span></button>)}</div>}
    {step === 'time' && service && <div>
      <button className="mb-4 underline" onClick={() => setStep('service')}>← {t(lang, 'varaus.back')}</button>
      <h2 className="mb-4 text-xl font-semibold">{service.name}</h2>
      <label className="block" htmlFor="booking-date">{t(lang, 'varaus.date')}<input id="booking-date" className={inputClass} type="date" min={minimum} value={date} onChange={e => { setDate(e.target.value); setSlot(''); }} /></label>
      {date && minimum && date < minimum && <p role="alert">{u.checkTime}</p>}
      {date && date >= minimum && <div className="my-5 grid grid-cols-3 gap-3 sm:grid-cols-4">
        {slotsError ? <div className="col-span-full"><p role="alert">{t(lang, 'varaus.slotsError')}</p><button className={buttonClass} onClick={() => setSlotsRetry(v => v + 1)}>{t(lang, 'varaus.retrySlots')}</button></div>
        : slots === null ? <p role="status" className="col-span-full">{t(lang, 'varaus.loadingSlots')}</p>
        : slots.length === 0 ? <p className="col-span-full">{t(lang, 'varaus.noSlots')}</p>
        : slots.map(s => <button key={s.startTime} className={inputClass} onClick={() => { setSlot(s.startTime); setStep('details'); }}>{s.startTime}</button>)}
      </div>}
    </div>}
    {step === 'details' && service && <form onSubmit={submit} noValidate>
      <fieldset disabled={submitting || failure === 'uncertain'}>
      <legend className="sr-only">{t(lang, 'varaus.detailsTitle')}</legend>
      <button type="button" className="mb-4 underline" onClick={() => setStep('time')}>← {t(lang, 'varaus.back')}</button>
      <h2 className="text-xl font-semibold">{t(lang, 'varaus.detailsTitle')}</h2><p className="my-4 rounded-lg border p-4">{summary}<br/>{u.price}{service.durationMinutes ? ` · ${u.duration}: ${service.durationMinutes} ${u.minutes}` : ''}</p>
      <div className="grid gap-4">{(['customerName','customerPhone','customerEmail','street','postalCode','city'] as const).map(key => {
        const label = { customerName: 'name', customerPhone: 'phone', customerEmail: 'email', street: 'street', postalCode: 'postalCode', city: 'city' }[key];
        const required = ['customerName','customerPhone','street','city'].includes(key);
        const autocomplete = { customerName: 'name', customerPhone: 'tel', customerEmail: 'email', street: 'street-address', postalCode: 'postal-code', city: 'address-level2' }[key];
        return <div key={key}><label htmlFor={key}>{t(lang, `varaus.${label}`)} ({required ? u.required : u.optional})</label><input className={inputClass} id={key} type={key === 'customerEmail' ? 'email' : key === 'customerPhone' ? 'tel' : 'text'} autoComplete={autocomplete} maxLength={key === 'customerPhone' ? 40 : key === 'street' ? 200 : key === 'postalCode' ? 20 : key === 'city' ? 100 : key === 'customerEmail' ? 254 : 150} required={required} value={form[key]} onChange={e => update(key,e.target.value)} aria-invalid={!!errors[key]} aria-describedby={errors[key] ? `${key}-error` : undefined}/>{errors[key] && <p id={`${key}-error`} className="text-red-700 dark:text-red-300">{errors[key]}</p>}</div>;
      })}
      <label htmlFor="notes">{t(lang, 'varaus.notes')}<textarea id="notes" className={inputClass} rows={3} maxLength={2000} value={form.notes} onChange={e => update('notes',e.target.value)} aria-describedby="notes-help"/></label><p id="notes-help" className="text-sm">{u.notes}</p>
      <p>{u.request}</p><a className="underline" target="_blank" rel="noopener noreferrer" href={baseUrl(`/${lang}/privacy/`)}>{u.privacy}</a><a className="underline" target="_blank" rel="noopener noreferrer" href={baseUrl(`/${lang}/terms/`)}>{u.terms}</a>
      <label className="flex items-start gap-3"><input id="accepted" className="mt-1 h-6 w-6 shrink-0" type="checkbox" checked={accepted} onChange={e => setAccepted(e.target.checked)} aria-invalid={!!errors.accepted}/>{u.accepted}</label>
      <button type="submit" disabled={submitting || failure === 'uncertain'} aria-busy={submitting} className={buttonClass}>{submitting ? t(lang, 'varaus.submitting') : t(lang, 'varaus.confirm')}</button>
      {failure === 'uncertain' && <a href="mailto:info@readysetsiivous.fi" className="underline">info@readysetsiivous.fi</a>}
      </div>
      </fieldset>
    </form>}
    {step === 'done' && result && <div className="space-y-4">
      <h2 className="text-2xl font-bold">{t(lang, 'varaus.doneTitle')}</h2><p id="booking-result-status">{u.pending}</p><p>{summary}</p><p>{u.zone}</p>
      <p>{t(lang, 'varaus.bookingNumber')}: <strong>{result.bookingNumber}</strong></p><p>{t(lang, 'varaus.reference')}: <strong>{result.customerReference}</strong></p>
      <label htmlFor="booking-management-key" className="block font-semibold">{t(lang, 'varaus.managementKey')}</label>
      <textarea id="booking-management-key" readOnly rows={3} value={result.managementKey} aria-describedby="management-key-help" spellCheck={false} className={`${inputClass} break-all font-mono text-sm`}/>
      <p id="management-key-help">{t(lang, 'varaus.managementKeyHelp')}</p><p>{u.keep}</p>
      <button className={buttonClass} onClick={async () => { try { await navigator.clipboard.writeText(result.managementKey); setCopyState('success'); } catch { setCopyState('failure'); } }}>{t(lang, 'varaus.copyManagementKey')}</button>
      <p role="status" aria-live="polite">{copyState === 'success' ? t(lang, 'varaus.managementKeyCopied') : copyState === 'failure' ? t(lang, 'varaus.managementKeyCopyFailed') : ''}</p>
      <button className={buttonClass} onClick={() => downloadConfirmation(`${u.pending}\n${summary}\n${u.zone}\n${u.number}: ${result.bookingNumber}\n${u.key}: ${result.managementKey}\n${u.keep}\nhttps://readysetsiivous.fi/${lang}/booking/\ninfo@readysetsiivous.fi`)}>{u.download}</button>
      <a className="block underline" href={baseUrl(`/${lang}/booking/`)}>{u.manage}</a><a className="block underline" href={baseUrl(`/${lang}/`)}>{t(lang, 'varaus.backHome')}</a>
    </div>}
  </div>;
}
