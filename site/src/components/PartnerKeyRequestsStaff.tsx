'use client';

import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { staffPartnerApi, type KeyRequestList } from '@/lib/partnerApi';

// Staff view of partner key requests and the one fee every new request is charged.
export function PartnerKeyRequestsStaff({ role }: { role: 'admin' | 'manager' }) {
  const [list, setList] = useState<KeyRequestList | null>(null);
  const [fee, setFee] = useState(''), [error, setError] = useState(''), [notice, setNotice] = useState(''), [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const next = await staffPartnerApi(role).keyRequests();
      setList(next); setFee(String(next.feeNet ?? 0)); setError('');
    } catch (e) { setError(e instanceof Error ? e.message : 'Key requests could not be loaded.'); }
  }, [role]);
  useEffect(() => { void refresh(); }, [refresh]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const value = Number(fee);
    if (busy || fee.trim() === '' || !Number.isFinite(value)) { setError('Enter the fee as a number.'); return; }
    setBusy(true); setError(''); setNotice('');
    try {
      const next = await staffPartnerApi(role).setKeyRequestFee(value);
      setList(next); setFee(String(next.feeNet ?? 0)); setNotice('Key request fee saved. New requests use this fee.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Fee could not be saved.'); }
    finally { setBusy(false); }
  }

  return <div className="space-y-4">
    <form onSubmit={save} aria-label="Key request fee" className="border rounded p-4 flex flex-wrap items-end gap-3">
      <label className="flex flex-col text-sm">Fee per key request, excl. VAT (EUR)
        <input className="border rounded px-3 py-2 w-40" type="number" min={0} max={10000} step="0.01" required value={fee} onChange={event => setFee(event.target.value)} />
      </label>
      <button className="border rounded px-4 py-2" disabled={busy} type="submit">Save fee</button>
    </form>
    {error && <p role="alert" className="text-red-600">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {list && <p className="text-sm">Current fee {Number(list.feeNet ?? 0).toFixed(2)} EUR excl. VAT; VAT {list.vatRatePercent}%.</p>}
    {!list?.items?.length ? <p>No key requests yet.</p> : <ul className="space-y-3">
      {list.items.map(item => <li key={item.id} className="border rounded p-3">
        <p className="font-medium">{item.branchName} · {item.kind === 'Handover' ? 'Key handover' : 'Key pickup'}</p>
        <p className="text-sm">{item.date} · {item.status} · {Number(item.feeNet ?? 0).toFixed(2)} EUR excl. VAT</p>
        {item.notes && <p className="text-sm">{item.notes}</p>}
        {item.status === 'Cancelled' && item.cancellationReason && <p className="text-sm">Cancelled: {item.cancellationReason}</p>}
      </li>)}
    </ul>}
  </div>;
}
