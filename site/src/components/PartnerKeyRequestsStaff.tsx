'use client';

import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { adminEmployees } from '@/lib/adminApi';
import { managerApi } from '@/lib/managerApi';
import { KeyRequestPhotos } from '@/components/KeyRequestPhotos';
import { staffPartnerApi, type KeyRequest, type KeyRequestList } from '@/lib/partnerApi';

type StaffEmployee = { id: string; name: string };

// Staff view of partner key requests: the fee, who collects the keys, completion and completion photos.
export function PartnerKeyRequestsStaff({ role }: { role: 'admin' | 'manager' }) {
  const [list, setList] = useState<KeyRequestList | null>(null);
  const [employees, setEmployees] = useState<StaffEmployee[]>([]);
  const [fee, setFee] = useState(''), [loadError, setLoadError] = useState(''), [error, setError] = useState(''), [notice, setNotice] = useState(''), [busy, setBusy] = useState(false);
  // Employee chosen in each request's dropdown, before Assign is pressed.
  const [choice, setChoice] = useState<Record<string, string>>({});
  // The fee input is loaded once. Later reloads must not overwrite a value the admin is typing.
  const feeLoaded = useRef(false);

  const refresh = useCallback(async () => {
    try {
      const next = await staffPartnerApi(role).keyRequests();
      setList(next); setLoadError('');
      if (!feeLoaded.current) { setFee(String(next.feeNet ?? 0)); feeLoaded.current = true; }
    } catch (e) { setLoadError(e instanceof Error ? e.message : 'Key requests could not be loaded.'); }
  }, [role]);
  useEffect(() => { void refresh(); }, [refresh]);

  // Staff directory for the assignee list. Partner accounts are never offered.
  useEffect(() => {
    const load = role === 'admin'
      ? adminEmployees.list().then(people => (people ?? []).filter(person => person.isActive && person.role !== 'partner').map(person => ({ id: person.id, name: `${person.firstName} ${person.lastName}`.trim() })))
      : managerApi.employees().then(people => people.filter(person => person.isActive).map(person => ({ id: person.id, name: `${person.firstName} ${person.lastName}`.trim() })));
    load.then(setEmployees, () => setEmployees([]));
  }, [role]);

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
        <input className="border rounded px-3 py-2 w-40" type="number" min={0} max={10000} step="0.01" required disabled={!list} value={fee} onChange={event => setFee(event.target.value)} />
      </label>
      <button className="border rounded px-4 py-2" disabled={busy || !list} type="submit">Save fee</button>
    </form>
    {loadError && <p role="alert" className="text-red-600">{loadError}</p>}
    {error && <p role="alert" className="text-red-600">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {list && <p className="text-sm">Current fee {Number(list.feeNet ?? 0).toFixed(2)} EUR excl. VAT; VAT {list.vatRatePercent}%.</p>}
    {!list?.items?.length ? <p>No key requests yet.</p> : <ul className="space-y-3">
      {list.items.map(item => <KeyRequestRow key={item.id} role={role} item={item} employees={employees} choice={choice[item.id] ?? item.assignedEmployeeId ?? ''} onChoice={value => setChoice(previous => ({ ...previous, [item.id]: value }))}
        busy={busy}
        onAssign={employeeId => { if (employeeId) void run(() => staffPartnerApi(role).assignKeyRequest(item, employeeId), 'Employee assigned.'); }}
        onComplete={() => void run(() => staffPartnerApi(role).completeKeyRequest(item), 'Key request marked completed.')}
        onPhoto={file => void run(() => staffPartnerApi(role).addKeyRequestPhoto(item, file), 'Photo added.')} />)}
    </ul>}
  </div>;
}

function KeyRequestRow({ role, item, employees, choice, onChoice, busy, onAssign, onComplete, onPhoto }: {
  role: 'admin' | 'manager'; item: KeyRequest; employees: StaffEmployee[]; choice: string; onChoice: (value: string) => void; busy: boolean;
  onAssign: (employeeId: string) => void; onComplete: () => void; onPhoto: (file: File) => void;
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
      <button className="border rounded px-3 py-2" type="button" disabled={busy || !choice || choice === item.assignedEmployeeId} onClick={() => onAssign(choice)}>Assign</button>
      <button className="border rounded px-3 py-2" type="button" disabled={busy || !item.assignedEmployeeId} onClick={onComplete}>Mark completed</button>
    </div>}
    {item.assignedEmployeeId && item.status !== 'Cancelled' && <KeyRequestPhotos heading="Photos" alt="Key request photo" photos={photos} busy={busy} onUpload={onPhoto}
      uploadLabel={`Add photo for ${item.branchName} ${item.date}`} loadUrl={photoId => staffPartnerApi(role).keyRequestPhotoUrl(item.id, photoId)} />}
  </li>;
}
