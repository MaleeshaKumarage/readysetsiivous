'use client';

import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { adminEmployees } from '@/lib/adminApi';
import { managerApi } from '@/lib/managerApi';
import { staffPartnerApi, type KeyRequest, type KeyRequestList } from '@/lib/partnerApi';

const photoLimit = 10;
const photoTypes = 'image/jpeg,image/png,image/webp';
type StaffEmployee = { id: string; name: string };

// Staff view of partner key requests: the fee, who collects the keys, completion and completion photos.
export function PartnerKeyRequestsStaff({ role }: { role: 'admin' | 'manager' }) {
  const [list, setList] = useState<KeyRequestList | null>(null);
  const [employees, setEmployees] = useState<StaffEmployee[]>([]);
  const [fee, setFee] = useState(''), [error, setError] = useState(''), [notice, setNotice] = useState(''), [busy, setBusy] = useState(false);
  // Employee chosen in each request's dropdown, before Assign is pressed.
  const [choice, setChoice] = useState<Record<string, string>>({});
  const [thumbs, setThumbs] = useState<Record<string, string>>({});
  const thumbRequests = useRef(new Set<string>()), thumbUrls = useRef<string[]>([]);

  const refresh = useCallback(async () => {
    try {
      const next = await staffPartnerApi(role).keyRequests();
      setList(next); setFee(String(next.feeNet ?? 0)); setError('');
    } catch (e) { setError(e instanceof Error ? e.message : 'Key requests could not be loaded.'); }
  }, [role]);
  useEffect(() => { void refresh(); }, [refresh]);

  // Staff directory for the assignee list. Partner accounts are never offered.
  useEffect(() => {
    const load = role === 'admin'
      ? adminEmployees.list().then(people => (people ?? []).filter(person => person.isActive && person.role !== 'partner').map(person => ({ id: person.id, name: `${person.firstName} ${person.lastName}`.trim() })))
      : managerApi.employees().then(people => people.filter(person => person.isActive).map(person => ({ id: person.id, name: `${person.firstName} ${person.lastName}`.trim() })));
    load.then(setEmployees, () => setEmployees([]));
  }, [role]);

  // Each photo is fetched once. Object URLs are released when the page leaves.
  useEffect(() => {
    for (const item of list?.items ?? []) for (const photo of item.photos ?? []) {
      if (thumbRequests.current.has(photo.id)) continue;
      thumbRequests.current.add(photo.id);
      staffPartnerApi(role).keyRequestPhotoUrl(item.id, photo.id).then(url => {
        thumbUrls.current.push(url);
        setThumbs(previous => ({ ...previous, [photo.id]: url }));
      }, () => thumbRequests.current.delete(photo.id));
    }
  }, [list, role]);
  useEffect(() => () => { thumbUrls.current.forEach(url => URL.revokeObjectURL(url)); }, []);

  async function run(work: () => Promise<unknown>, success: string) {
    if (busy) return;
    setBusy(true); setError(''); setNotice('');
    try { await work(); setNotice(success); }
    catch (e) { setError(e instanceof Error ? e.message : 'The change could not be saved. Refresh before retrying.'); }
    finally { setBusy(false); await refresh(); }
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const value = Number(fee);
    if (busy || fee.trim() === '' || !Number.isFinite(value)) { setError('Enter the fee as a number.'); return; }
    await run(async () => { const next = await staffPartnerApi(role).setKeyRequestFee(value); setList(next); setFee(String(next.feeNet ?? 0)); }, 'Key request fee saved. New requests use this fee.');
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
      {list.items.map(item => <KeyRequestRow key={item.id} item={item} employees={employees} choice={choice[item.id] ?? item.assignedEmployeeId ?? ''} onChoice={value => setChoice(previous => ({ ...previous, [item.id]: value }))}
        busy={busy} thumbs={thumbs}
        onAssign={() => { const employeeId = choice[item.id]; if (employeeId) void run(() => staffPartnerApi(role).assignKeyRequest(item, employeeId), 'Employee assigned.'); }}
        onComplete={() => void run(() => staffPartnerApi(role).completeKeyRequest(item), 'Key request marked completed.')}
        onPhoto={file => void run(() => staffPartnerApi(role).addKeyRequestPhoto(item, file), 'Photo added.')} />)}
    </ul>}
  </div>;
}

function KeyRequestRow({ item, employees, choice, onChoice, busy, thumbs, onAssign, onComplete, onPhoto }: {
  item: KeyRequest; employees: StaffEmployee[]; choice: string; onChoice: (value: string) => void; busy: boolean; thumbs: Record<string, string>;
  onAssign: () => void; onComplete: () => void; onPhoto: (file: File) => void;
}) {
  const open = item.status === 'Requested';
  const completed = item.status === 'Completed';
  const photos = item.photos ?? [];
  return <li className="border rounded p-3 space-y-2">
    <p className="font-medium">{item.branchName} · {item.kind === 'Handover' ? 'Key handover' : 'Key pickup'} · {item.date}</p>
    <p className="text-sm">{item.status} · {Number(item.feeNet ?? 0).toFixed(2)} EUR excl. VAT{item.assignedEmployeeName ? ` · Assigned to ${item.assignedEmployeeName}` : ' · Not assigned'}</p>
    {item.notes && <p className="text-sm">{item.notes}</p>}
    {item.status === 'Cancelled' && item.cancellationReason && <p className="text-sm">Cancelled: {item.cancellationReason}</p>}
    {open && <div className="flex flex-wrap items-end gap-2">
      <label className="flex flex-col text-sm">Collected or handed over by
        <select className="border rounded px-3 py-2" value={choice} onChange={event => onChoice(event.target.value)}>
          <option value="">Choose a staff member</option>
          {employees.map(person => <option key={person.id} value={person.id}>{person.name}</option>)}
        </select>
      </label>
      <button className="border rounded px-3 py-2" type="button" disabled={busy || !choice || choice === item.assignedEmployeeId} onClick={onAssign}>Assign</button>
      <button className="border rounded px-3 py-2" type="button" disabled={busy || !item.assignedEmployeeId} onClick={onComplete}>Mark completed</button>
    </div>}
    {completed && <div className="space-y-2">
      <p className="text-sm">Photos {photos.length}/{photoLimit}</p>
      {photos.length < photoLimit && <label className="flex flex-col text-sm">Add photo
        <input type="file" accept={photoTypes} disabled={busy} aria-label={`Add photo for ${item.branchName} ${item.date}`}
          onChange={event => { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ''; if (file) onPhoto(file); }} />
      </label>}
      {photos.length > 0 && <ul className="flex flex-wrap gap-2">{photos.map(photo => <li key={photo.id}>
        {thumbs[photo.id] ? <img src={thumbs[photo.id]} alt={`Key request photo ${photo.id.slice(0, 8)}`} className="w-24 h-24 object-cover rounded border" /> : <span className="text-sm">Loading photo…</span>}
      </li>)}</ul>}
    </div>}
  </li>;
}
