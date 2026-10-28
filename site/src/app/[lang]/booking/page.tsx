'use client';

import { useEffect, useRef, useState, type FormEvent } from 'react';
import Navbar from '@/components/Navbar';
import Footer from '@/components/Footer';
import { publicPost, type ManagedBooking, type PublicFailure } from '@/lib/api';
import { publicUx } from '@/i18n/publicUx';
import { type Language, DEFAULT_LANGUAGE } from '@/i18n';
import { fullDate, money, downloadConfirmation } from '@/lib/publicFormatting';

export default function BookingManagement({ params }: { params: { lang: string } }) {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE, u = publicUx(lang);
  const [number, setNumber] = useState(''), [key, setKey] = useState('');
  const [booking, setBooking] = useState<ManagedBooking | null>(null);
  const [error, setError] = useState<PublicFailure | null>(null), [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState(false), [acknowledged, setAcknowledged] = useState(false);
  const [cancelUncertain, setCancelUncertain] = useState(false);
  const resultHeading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { resultHeading.current?.focus(); }, [booking?.bookingNumber]);
  useEffect(() => { if (!busy && error) document.getElementById(error === 'notFound' ? 'manage-key' : 'manage-number')?.focus(); }, [busy,error]);
  const input = 'w-full rounded-lg border border-gray-400 dark:border-gray-500 bg-white dark:bg-gray-800 p-3';
  const button = 'rounded-full bg-brand-500 px-6 py-3 font-semibold text-accent-950 disabled:opacity-60';
  async function lookup(event: FormEvent) {
    event.preventDefault(); if (busy) return;
    setBusy(true); setError(null); setBooking(null); setConfirm(false); setAcknowledged(false);
    const response = await publicPost<ManagedBooking>('/bookings/lookup', { bookingNumber: number.trim(), managementKey: key.trim() });
    setBusy(false);
    if (response.error) setError(response.error);
    else { setBooking(response.value); setCancelUncertain(false); }
  }
  async function cancel() {
    if (!booking || busy || cancelUncertain) return;
    setBusy(true); setError(null);
    const response = await publicPost<void>(`/bookings/${encodeURIComponent(booking.bookingNumber)}/cancel`, { managementKey: key.trim() });
    setBusy(false);
    if (response.error) { setError(response.error); setCancelUncertain(response.error === 'uncertain'); }
    else { setBooking({ ...booking, status: 'Cancelled' }); setAcknowledged(true); setConfirm(false); }
  }
  const serviceName = booking && (lang === 'en' ? booking.serviceNameEn : lang === 'sv' ? booking.serviceNameSv : booking.serviceNameFi) || booking?.serviceNameFi;
  const status = booking?.status.toLowerCase();
  const statusLabel = status === 'cancelled' ? u.cancelled : status === 'completed' ? u.completed : status === 'confirmed' ? u.confirmed : u.pending;
  return <><Navbar lang={lang}/><main tabIndex={-1} id="main-content" className="container-page max-w-2xl py-12">
    <h1 className="heading-lg mb-6">{u.manage}</h1><p className="mb-4">{u.keep}</p>
    <form onSubmit={lookup} className="grid gap-4">
      <fieldset disabled={busy} className="grid gap-4">
      <legend className="sr-only">{u.lookup}</legend>
      <label htmlFor="manage-number">{u.number}<input className={input} id="manage-number" autoComplete="off" required maxLength={100} value={number} onChange={e => { setNumber(e.target.value); setBooking(null); }}/></label>
      <label htmlFor="manage-key">{u.key}<input className={input} id="manage-key" type="password" autoComplete="off" spellCheck={false} required maxLength={256} value={key} onChange={e => { setKey(e.target.value); setBooking(null); }}/></label>
      <button className={button} disabled={busy} aria-busy={busy}>{u.lookup}</button>
      </fieldset>
    </form>
    {error && <p role="alert" className="my-4 border border-red-600 p-3">{u[error]}</p>}
    {booking && <section aria-live="polite" className="my-6 space-y-4 rounded-xl border p-5">
      <h2 ref={resultHeading} tabIndex={-1} className="text-xl font-bold">{booking.bookingNumber}</h2><p>{serviceName}</p><p>{fullDate(booking.startLocalDate,lang)} · {booking.startLocalTime}</p><p>{u.zone}</p>
      {booking.durationMinutes && <p>{u.duration}: {booking.durationMinutes} {u.minutes}</p>}
      {booking.priceGross !== undefined && <p>{u.price}: {money(booking.priceGross,lang,booking.currency ?? 'EUR')}</p>}
      <p>{u.status}: {statusLabel}</p>
      {acknowledged && <><p>{u.cancelDone}</p><button className={button} onClick={() => downloadConfirmation(`${u.cancelDone}\n${u.number}: ${booking.bookingNumber}\n${fullDate(booking.startLocalDate,lang)} ${booking.startLocalTime}\n${u.zone}\n${serviceName}\n${u.status}: ${u.cancelled}\ninfo@readysetsiivous.fi`)}>{u.acknowledgement}</button></>}
      {!['cancelled','completed'].includes(status ?? '') && !confirm && <button className={button} onClick={() => setConfirm(true)}>{u.cancel}</button>}
      {confirm && <div className="space-y-3"><p>{u.cancelHelp}</p><button className={button} disabled={busy || cancelUncertain} onClick={cancel}>{u.cancelConfirm}</button></div>}
    </section>}
    <button disabled={busy} className="my-4 underline" onClick={() => { setNumber(''); setKey(''); setBooking(null); setError(null); setConfirm(false); setAcknowledged(false); setCancelUncertain(false); }}>{u.clear}</button>
    <p><a className="underline" href="mailto:info@readysetsiivous.fi">info@readysetsiivous.fi</a></p>
  </main><Footer/></>;
}
