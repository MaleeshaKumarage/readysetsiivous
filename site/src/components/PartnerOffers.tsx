'use client';
import { useCallback, useEffect, useState } from 'react';
import { offerApi, type CleanerOffer } from '@/lib/partnerApi';

export function PartnerOffers({ onAccepted }: { onAccepted: () => void }) {
  const [offers, setOffers] = useState<CleanerOffer[]>([]), [error, setError] = useState(''), [loading, setLoading] = useState(true), [busy, setBusy] = useState(false);
  const [times, setTimes] = useState<Record<string, string>>({}), [reasons, setReasons] = useState<Record<string, string>>({});
  const load = useCallback(async () => { setLoading(true); setError(''); try { setOffers(await offerApi.list()); } catch (e) { setError(e instanceof Error ? e.message : 'Could not load cleaning offers.'); } finally { setLoading(false); } }, []);
  useEffect(() => { void load(); }, [load]);
  async function decide(offer: CleanerOffer, accept: boolean) {
    if (busy) return;
    if (accept && !offer.plannedStartUtc && !times[offer.id]) { setError('Choose a planned start time in the requested window.'); return; }
    setBusy(true); setError('');
    try { if (accept) { await offerApi.accept(offer, times[offer.id]); onAccepted(); } else await offerApi.decline(offer, reasons[offer.id]); await load(); }
    catch (e) { setError(e instanceof Error ? e.message : 'Your decision could not be confirmed. Refresh offers before retrying.'); }
    finally { setBusy(false); }
  }
  return <section aria-busy={busy || loading} aria-label="Cleaning offers" className="bg-white border rounded-lg p-4 space-y-3 text-gray-900 [&_button]:min-h-11 [&_input]:min-h-11"><h2 className="text-lg font-semibold">Cleaning offers awaiting your acceptance</h2>{loading && <p role="status">Loading offers…</p>}{busy && <p role="status">Saving your decision…</p>}{error && <p role="alert" className="text-red-700">{error}</p>}<button disabled={busy || loading} className="underline" onClick={() => void load()}>Refresh offers</button>{!loading && !offers.length && !error && <p>No pending cleaning offers.</p>}{offers.map(offer => <article aria-label={`${offer.branchName} offer`} className="border rounded p-4 space-y-2" key={offer.id}><h3 className="font-semibold">{offer.branchName}</h3><p>{[offer.street, offer.postalCode, offer.city].filter(Boolean).join(', ')}</p><p>{offer.fromDate} – {offer.toDate} · Estimated {offer.estimatedDurationMinutes} minutes · {offer.timeZoneId}</p>{offer.notes && <p className="whitespace-pre-wrap">{offer.notes}</p>}{offer.plannedStartUtc ? <p>Planned start: {new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short', timeZone: offer.timeZoneId }).format(new Date(offer.plannedStartUtc))}</p> : <label>Planned start ({offer.timeZoneId})<input className="block border rounded p-2 bg-white" aria-label={`Planned start for ${offer.branchName}`} type="datetime-local" min={`${offer.fromDate}T00:00`} max={`${offer.toDate}T23:59`} value={times[offer.id] ?? ''} onChange={e => setTimes(current => ({ ...current, [offer.id]: e.target.value }))} /></label>}<label>Decline reason (optional)<input className="block border rounded p-2 bg-white" value={reasons[offer.id] ?? ''} onChange={e => setReasons(current => ({ ...current, [offer.id]: e.target.value }))} /></label><div className="flex gap-4"><button disabled={busy} className="rounded bg-gray-900 text-white px-4 py-2 disabled:opacity-50" onClick={() => void decide(offer, true)}>Accept cleaning</button><button disabled={busy} className="underline" onClick={() => void decide(offer, false)}>Decline cleaning</button></div></article>)}</section>;
}
