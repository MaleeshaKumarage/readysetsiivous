'use client';

import { useEffect, useState } from 'react';
import { authorizedFetch } from '@/lib/adminApi';
import { supplyRequests, type SupplyRequest } from '@/lib/supplyRequestApi';

interface CatalogueItem { id: string; name: string; unit: string; isActive: boolean }

const MAX_QUANTITY = 1000;

// Employees ask for catalogue items. Quantities are typed per item; only items with a quantity above zero are sent.
export function SupplyRequestPanel() {
  const [items, setItems] = useState<CatalogueItem[] | null>(null);
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [note, setNote] = useState('');
  const [history, setHistory] = useState<SupplyRequest[] | null>(null);
  const [loadError, setLoadError] = useState('');
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);

  async function loadAll() {
    setLoadError('');
    try {
      const [catalogue, own] = await Promise.all([
        authorizedFetch('/api/v1/me/inventory/items', { cache: 'no-store' }).then(async r => {
          if (!r.ok) throw new Error('Items could not be loaded.');
          return r.json() as Promise<CatalogueItem[]>;
        }),
        supplyRequests.own(),
      ]);
      setItems(catalogue.filter(item => item.isActive));
      setHistory(own);
    } catch (e) {
      setLoadError(e instanceof Error ? e.message : 'Supply requests could not be loaded.');
    }
  }

  useEffect(() => { void loadAll(); }, []);

  const chosen = (items ?? []).flatMap(item => {
    const quantity = Number(quantities[item.id] ?? '');
    return Number.isInteger(quantity) && quantity > 0 ? [{ itemId: item.id, quantity }] : [];
  });

  async function send() {
    if (busy || chosen.length === 0) return;
    setBusy(true); setError(''); setNotice('');
    try {
      await supplyRequests.create(chosen, note);
      setNotice('Request sent. An admin or manager will review it.');
      setQuantities({}); setNote('');
      await loadAll();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The request could not be sent.');
    } finally {
      setBusy(false);
    }
  }

  return <div className="space-y-6">
    <section aria-labelledby="supply-request-heading" className="space-y-4">
      <h2 id="supply-request-heading" className="text-lg font-semibold">Request supplies</h2>
      {loadError && <p role="alert" className="text-red-700 dark:text-red-300">{loadError} <button type="button" className="underline" onClick={() => void loadAll()}>Try again</button></p>}
      {!items && !loadError && <p className="text-sm text-gray-500">Loading…</p>}
      {items && items.length === 0 && <p className="text-sm text-gray-500">No items are available to request yet.</p>}
      {items && items.length > 0 && <ul className="space-y-3">
        {items.map(item => <li key={item.id} className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-l-4 border-l-brand-400 border-gray-200 bg-white p-4 shadow-sm dark:border-gray-700 dark:bg-gray-900">
          <span className="font-medium">{item.name} <span className="text-sm text-gray-500">({item.unit})</span></span>
          <label className="flex items-center gap-2 text-sm">
            <span className="sr-only">Quantity for {item.name}</span>
            <span aria-hidden="true">Qty</span>
            <input type="number" inputMode="numeric" min={0} max={MAX_QUANTITY} step={1} aria-label={`Quantity for ${item.name}`}
              value={quantities[item.id] ?? ''} onChange={e => setQuantities(current => ({ ...current, [item.id]: e.target.value }))}
              className="min-h-11 w-24 rounded-lg border border-gray-300 px-3 dark:border-gray-600 dark:bg-gray-800" />
          </label>
        </li>)}
      </ul>}
      {items && items.length > 0 && <label className="flex flex-col gap-1 text-sm font-medium">
        Note (optional)
        <textarea aria-label="Note (optional)" value={note} onChange={e => setNote(e.target.value)} maxLength={500} rows={2}
          className="rounded-lg border border-gray-300 p-2 dark:border-gray-600 dark:bg-gray-800" />
      </label>}
      {error && <p role="alert" className="text-sm text-red-700 dark:text-red-300">{error}</p>}
      {notice && <p role="status" className="text-sm text-emerald-700 dark:text-emerald-300">{notice}</p>}
      {items && items.length > 0 && <button type="button" disabled={busy || chosen.length === 0} onClick={() => void send()}
        className="min-h-11 w-full rounded-full bg-gray-900 px-4 py-2 font-semibold text-white disabled:opacity-50 dark:bg-gray-100 dark:text-gray-900">
        {busy ? 'Sending…' : 'Send request'}
      </button>}
    </section>

    <section aria-labelledby="supply-history-heading" className="space-y-3">
      <h2 id="supply-history-heading" className="text-lg font-semibold">My supply requests</h2>
      {history === null && !loadError && <p className="text-sm text-gray-500">Loading…</p>}
      {history !== null && history.length === 0 && <p className="text-sm text-gray-500">No supply requests yet.</p>}
      {history !== null && history.length > 0 && <ul className="space-y-3">
        {history.map(request => <li key={request.id} className="rounded-2xl border border-l-4 border-l-brand-400 border-gray-200 bg-white p-4 shadow-sm dark:border-gray-700 dark:bg-gray-900">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="text-sm">{request.lines.map(line => `${line.quantity} ${line.unit} ${line.itemName}`).join(', ')}</span>
            <span className="rounded-full border px-2.5 py-1 text-xs font-semibold">{request.status}</span>
          </div>
          {request.note && <p className="mt-1 text-sm text-gray-600 dark:text-gray-300">{request.note}</p>}
          {request.decisionNote && <p className="mt-1 text-sm">Note from the office: {request.decisionNote}</p>}
        </li>)}
      </ul>}
    </section>
  </div>;
}
