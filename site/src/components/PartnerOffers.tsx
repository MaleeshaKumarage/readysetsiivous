'use client';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Card } from '@/ds/Card';
import { Button } from '@/ds/Button';
import { Field, Input } from '@/ds/Input';
import { offerApi, type CleanerOffer } from '@/lib/partnerApi';

export function PartnerOffers({ onAccepted, hideWhenEmpty = false }: { onAccepted: () => void; hideWhenEmpty?: boolean }) {
  const [offers, setOffers] = useState<CleanerOffer[]>([]), [error, setError] = useState(''), [loading, setLoading] = useState(true), [busy, setBusy] = useState(false);
  const [times, setTimes] = useState<Record<string, string>>({}), [reasons, setReasons] = useState<Record<string, string>>({});
  const loadVersion = useRef(0);
  const load = useCallback(async () => {
    const version = ++loadVersion.current; setLoading(true); setError('');
    try { const result = await offerApi.list(); if (version === loadVersion.current) setOffers(result); }
    catch (e) { if (version === loadVersion.current) setError(e instanceof Error ? e.message : 'Could not load cleaning offers.'); }
    finally { if (version === loadVersion.current) setLoading(false); }
  }, []);
  useEffect(() => { void load(); return () => { loadVersion.current++; }; }, [load]);
  useEffect(() => {
    const refresh = () => { if (!busy && document.visibilityState === 'visible') void load(); };
    window.addEventListener('focus', refresh); document.addEventListener('visibilitychange', refresh);
    return () => { window.removeEventListener('focus', refresh); document.removeEventListener('visibilitychange', refresh); };
  }, [busy, load]);
  async function decide(offer: CleanerOffer, accept: boolean) {
    if (busy) return;
    if (accept && !offer.plannedStartUtc && !times[offer.id]) { setError('Choose a planned start time in the requested window.'); return; }
    setBusy(true); setError('');
    try { if (accept) { await offerApi.accept(offer, times[offer.id]); onAccepted(); } else await offerApi.decline(offer, reasons[offer.id]); await load(); }
    catch (e) { setError(e instanceof Error ? e.message : 'Your decision could not be confirmed. Refresh offers before retrying.'); }
    finally { setBusy(false); }
  }
  if (hideWhenEmpty && !loading && !busy && !error && !offers.length) return null;
  return <section aria-busy={busy || loading} aria-label="Cleaning offers" className="space-y-4 text-gray-900 dark:text-gray-100">
    <Card className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">Cleaning offers awaiting your acceptance</h2>
        <Button variant="ghost" size="sm" disabled={busy || loading} onClick={() => void load()}>Refresh offers</Button>
      </div>
      {loading && <p role="status" className="text-sm text-gray-600 dark:text-gray-300">Loading offers…</p>}
      {busy && <p role="status" className="text-sm text-gray-600 dark:text-gray-300">Saving your decision…</p>}
      {error && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-950 dark:text-red-200">{error}</p>}
      {!loading && !offers.length && !error && <p className="text-sm text-gray-600 dark:text-gray-300">No pending cleaning offers.</p>}
    </Card>
    {offers.map(offer => <article aria-label={`${offer.branchName} offer`} key={offer.id}>
      <Card className="space-y-4">
        <h3 className="text-lg font-semibold break-words">{offer.branchName}</h3>
        <p className="text-sm text-gray-600 dark:text-gray-300 break-words">{[offer.street, offer.postalCode, offer.city].filter(Boolean).join(', ')}</p>
        <p className="text-sm text-gray-600 dark:text-gray-300">{offer.fromDate} – {offer.toDate} · Estimated {offer.estimatedDurationMinutes} minutes · {offer.timeZoneId}</p>
        {offer.notes && <p className="whitespace-pre-wrap break-words">{offer.notes}</p>}
        {offer.plannedStartUtc
          ? <p>Planned start: {new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short', timeZone: offer.timeZoneId }).format(new Date(offer.plannedStartUtc))}</p>
          : <Field label={`Planned start (${offer.timeZoneId})`}><Input aria-label={`Planned start for ${offer.branchName}`} type="datetime-local" min={`${offer.fromDate}T00:00`} max={`${offer.toDate}T23:59`} value={times[offer.id] ?? ''} onChange={e => setTimes(current => ({ ...current, [offer.id]: e.target.value }))} /></Field>}
        <Field label="Decline reason (optional)"><Input value={reasons[offer.id] ?? ''} onChange={e => setReasons(current => ({ ...current, [offer.id]: e.target.value }))} /></Field>
        <div className="flex flex-wrap gap-3">
          <Button disabled={busy} onClick={() => void decide(offer, true)}>Accept cleaning</Button>
          <Button variant="outline" disabled={busy} onClick={() => void decide(offer, false)}>Decline cleaning</Button>
        </div>
      </Card>
    </article>)}
  </section>;
}
