'use client';
import { useCallback, useEffect, useState } from 'react';
import { Card } from '@/ds/Card';
import { Button } from '@/ds/Button';
import { Field, Input } from '@/ds/Input';
import { offerApi, type CleanerOffer } from '@/lib/partnerApi';

const startFormat = new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Europe/Helsinki' });
export function PartnerOffers({ onAccepted }: { onAccepted: () => void }) {
  const [offers, setOffers] = useState<CleanerOffer[]>([]), [error, setError] = useState(''), [loading, setLoading] = useState(true), [busy, setBusy] = useState(false);
  const [times, setTimes] = useState<Record<string, string>>({}), [reasons, setReasons] = useState<Record<string, string>>({}), [notice, setNotice] = useState('');
  const load = useCallback(async () => { setLoading(true); setError(''); try { setOffers(await offerApi.list()); } catch (e) { setError(e instanceof Error ? e.message : 'Could not load cleaning offers.'); } finally { setLoading(false); } }, []);
  useEffect(() => { void load(); }, [load]);
  async function decide(offer: CleanerOffer, accept: boolean) {
    if (busy) return;
    setBusy(true); setError(''); setNotice('');
    try {
      if (accept) {
        await offerApi.accept(offer, times[offer.id]);
        const planned = offer.plannedStartUtc ? startFormat.format(new Date(offer.plannedStartUtc)) : times[offer.id]?.replace('T', ' ');
        setNotice(planned
          ? `Cleaning accepted for ${offer.branchName}, starting ${planned}.`
          : `Cleaning accepted for ${offer.branchName}. Choose your start any time from ${offer.fromDate} to ${offer.toDate}.`);
        onAccepted();
      } else {
        await offerApi.decline(offer, reasons[offer.id]);
        setNotice(`Offer declined for ${offer.branchName}. Staff have been told.`);
      }
      await load();
    }
    catch (e) { setError(e instanceof Error ? e.message : 'Your decision could not be confirmed. Refresh offers before retrying.'); }
    finally { setBusy(false); }
  }
  return <section aria-busy={busy || loading} aria-label="Cleaning offers" className="space-y-4 text-gray-900 dark:text-gray-100">
    <Card className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">Cleaning offers awaiting your acceptance</h2>
        <Button variant="ghost" size="sm" disabled={busy || loading} onClick={() => void load()}>Refresh offers</Button>
      </div>
      {loading && <p role="status" className="text-sm text-gray-600 dark:text-gray-300">Loading offers…</p>}
      {busy && <p role="status" className="text-sm text-gray-600 dark:text-gray-300">Saving your decision…</p>}
      {notice && <p role="status" className="rounded-xl border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-200">{notice}</p>}
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
          : <><Field label={`Planned start (${offer.timeZoneId}, optional)`}><Input aria-label={`Planned start for ${offer.branchName}`} type="datetime-local" min={`${offer.fromDate}T00:00`} max={`${offer.toDate}T23:59`} value={times[offer.id] ?? ''} onChange={e => setTimes(current => ({ ...current, [offer.id]: e.target.value }))} /></Field><p className="text-sm text-gray-600 dark:text-gray-300">Leave blank to choose your start any time from {offer.fromDate} to {offer.toDate}. The visit takes {offer.estimatedDurationMinutes} minutes and must finish by the end of {offer.toDate}.</p></>}
        <Field label="Decline reason (optional)"><Input value={reasons[offer.id] ?? ''} onChange={e => setReasons(current => ({ ...current, [offer.id]: e.target.value }))} /></Field>
        <div className="flex flex-wrap gap-3">
          <Button disabled={busy} onClick={() => void decide(offer, true)}>Accept cleaning</Button>
          <Button variant="outline" disabled={busy} onClick={() => void decide(offer, false)}>Decline cleaning</Button>
        </div>
      </Card>
    </article>)}
  </section>;
}
