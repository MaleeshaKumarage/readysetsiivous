'use client';
import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import Link from 'next/link';
import { authTenant, initAuth, isAuthenticated, isPartner, isAdmin, isManager, login, logout } from '@/lib/auth';
import { partnerApi, type CleaningRequest, type PartnerIdentity, type PartnerShift, type PartnerChange } from '@/lib/partnerApi';
import type { Branch } from '@/lib/adminApi';
import { QuoteRequestedBadge } from '@/components/QuoteRequests';

const input = 'block w-full border border-gray-300 rounded px-3 py-2 bg-white text-gray-900';
const button = 'rounded bg-gray-900 text-white px-4 py-2 disabled:opacity-50';
export default function PartnerPage() {
  const [identity, setIdentity] = useState<PartnerIdentity | null>(null), [branches, setBranches] = useState<Branch[]>([]), [requests, setRequests] = useState<CleaningRequest[]>([]), [shifts, setShifts] = useState<PartnerShift[]>([]);
  const [changes, setChanges] = useState<PartnerChange[]>([]);
  const [access, setAccess] = useState('pending'), [error, setError] = useState(''), [busy, setBusy] = useState(false), [notice, setNotice] = useState('');
  const [tab, setTab] = useState('Properties'), [adding, setAdding] = useState(false), [target, setTarget] = useState<Branch | null>(null), [editing, setEditing] = useState<CleaningRequest | null>(null);
  const [from, setFrom] = useState(''), [to, setTo] = useState(''), [exact, setExact] = useState(false), [time, setTime] = useState(''), [notes, setNotes] = useState('');
  const [change, setChange] = useState<{ kind: 'branch' | 'request'; id: string } | null>(null), [message, setMessage] = useState('');
  const generation = useRef(0), requestKey = useRef('');
  const refresh = useCallback(async () => {
    const attempt = ++generation.current; setError('');
    try {
      await initAuth(); if (attempt !== generation.current) return;
      if (!isAuthenticated()) { setAccess('anonymous'); return; }
      if (!isPartner() || isAdmin() || isManager()) { setAccess('denied'); return; }
      const person = await partnerApi.me();
      if (person.realm !== authTenant() || person.person.role !== 'partner' || !person.person.isActive || !person.company.isActive || !person.company.isPartnerEnabled) { setAccess('denied'); return; }
      const [properties, work, scheduled, changeHistory] = await Promise.all([partnerApi.branches(), partnerApi.requests(), partnerApi.shifts(), partnerApi.changes()]);
      if (attempt !== generation.current) return;
      setIdentity(person); setBranches(properties); setRequests(work); setShifts(scheduled); setChanges(changeHistory); setAccess('allowed');
    } catch (e) { if (attempt === generation.current) { setError(e instanceof Error ? e.message : 'Could not load your company.'); setAccess('error'); } }
  }, []);
  useEffect(() => { void refresh(); return () => { generation.current++; }; }, [refresh]);
  async function act(action: () => Promise<unknown>, success: string) {
    if (busy) return; setBusy(true); setError(''); setNotice('');
    try { await action(); setNotice(success); await refresh(); }
    catch (e) { setError(e instanceof Error ? e.message : 'Your change could not be confirmed. Refresh before retrying.'); }
    finally { setBusy(false); }
  }
  function openRequest(branch: Branch, request?: CleaningRequest) {
    setTarget(branch); setEditing(request ?? null); setFrom(request?.fromDate ?? ''); setTo(request?.toDate ?? ''); setExact(Boolean(request?.exactStartLocalTime)); setTime(request?.exactStartLocalTime?.slice(0, 5) ?? ''); setNotes(request?.notes ?? ''); requestKey.current = crypto.randomUUID(); setError('');
  }
  async function submitRequest(e: FormEvent) {
    e.preventDefault(); if (!target) return;
    await act(async () => {
      const body = { fromDate: from, toDate: exact ? from : to || from, exactStartLocalTime: exact ? time : null, notes };
      if (editing) await partnerApi.updateRequest(editing.id, { ...body, version: editing.version });
      else await partnerApi.createRequest({ ...body, branchId: target.id, clientRequestId: requestKey.current });
      setTarget(null); setEditing(null); setTab('Cleaning requests');
    }, 'Cleaning request saved. Staff will arrange a cleaner.');
  }
  const stamp = (value: string | null) => value ? new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short', timeZone: identity?.timeZoneId }).format(new Date(value)) : 'Not recorded';
  return <main className="min-h-screen bg-gray-50 text-gray-900 p-4 sm:p-8"><div className="max-w-5xl mx-auto space-y-5 [&_button]:min-h-11 [&_input]:min-h-11">
    <header className="flex flex-wrap justify-between gap-4"><div><h1 className="text-2xl font-bold">Partner portal</h1>{identity && <p>{identity.company.name} · Times in {identity.timeZoneId}</p>}</div><nav className="flex gap-4"><Link className="underline" href="/account/">Account</Link>{access !== 'anonymous' && <button onClick={() => logout()}>Sign out</button>}</nav></header>
    {access === 'pending' && <p role="status">Checking access…</p>}
    {access === 'anonymous' && <button className={button} onClick={() => login()}>Sign in</button>}
    {access === 'denied' && <p role="alert">Access denied. An active partner account linked to an enabled company is required.</p>}
    {busy && <p role="status">Saving changes…</p>}{error && <p role="alert" className="text-red-700">{error}</p>}{notice && <p role="status">{notice}</p>}
    {['allowed', 'error'].includes(access) && <button disabled={busy} className="underline" onClick={() => void refresh()}>Refresh partner data</button>}
    {access === 'allowed' && <>
      <div role="tablist" aria-label="Partner sections" className="flex flex-wrap gap-3">{['Properties', 'Cleaning requests', 'Company shifts', 'Change requests'].map(name => <button key={name} role="tab" aria-selected={tab === name} className={tab === name ? button : 'px-4 py-2 border rounded'} onClick={() => setTab(name)}>{name}</button>)}</div>
      {tab === 'Properties' && <section className="space-y-4"><button className={button} disabled={busy} onClick={() => setAdding(true)}>Add property</button>{!branches.length && <p>No properties yet. Add a property and request a quote.</p>}{branches.map(branch => <article aria-label={branch.name} key={branch.id} className="bg-white border rounded-lg p-5 space-y-3"><h2 className="font-semibold text-lg">{branch.name}</h2><QuoteRequestedBadge branch={branch} /><p>{[branch.street, branch.postalCode, branch.city].filter(Boolean).join(', ')}</p><p>Rooms: {branch.roomCount ?? 'Not set'} · Beds: {branch.bedCount ?? 'Not set'}</p><p>{branch.pricingReady ? `€${branch.priceNet?.toFixed(2)} excl. VAT (${branch.vatRatePercent}%) · Estimated ${branch.estimatedDurationMinutes} minutes` : branch.quoteRequestedUtc ? 'Quote requested — awaiting staff pricing' : 'Pricing required before requesting cleaning'}</p><div className="flex flex-wrap gap-4"><button className={button} disabled={busy || !branch.isActive || !branch.pricingReady} onClick={() => openRequest(branch)}>Request cleaning</button>{!branch.pricingReady && <button disabled={busy || !branch.isActive || Boolean(branch.quoteRequestedUtc)} className="underline" onClick={() => act(() => partnerApi.quote(branch.id), 'Quote requested.')}>Request quote</button>}<button className="underline" onClick={() => { setChange({ kind: 'branch', id: branch.id }); setMessage(''); }}>Request property changes</button></div></article>)}</section>}
      {tab === 'Cleaning requests' && <section className="space-y-4">{!requests.length && <p>No cleaning requests yet.</p>}{requests.map(request => <article key={request.id} aria-label={`${request.branchName} ${request.fromDate}`} className="bg-white border rounded-lg p-5 space-y-2"><h2 className="font-semibold">{request.branchName} · {request.status}</h2><p>{request.fromDate} – {request.toDate}{request.exactStartLocalTime && ` at ${request.exactStartLocalTime.slice(0, 5)}`}</p><p>Agreed price €{request.priceNet.toFixed(2)} excl. VAT ({request.vatRatePercent}%)</p><p>Cleaner: {request.acceptedCleanerName ?? 'Awaiting acceptance'}</p><p>Planned: {stamp(request.plannedStartUtc)} – {stamp(request.plannedEndUtc)}</p><p>Actual start: {stamp(request.actualStartUtc)} · Actual end: {stamp(request.actualEndUtc)}</p>{request.notes && <p className="whitespace-pre-wrap">{request.notes}</p>}<div className="flex gap-4 flex-wrap">{!request.firstAcceptedAtUtc && request.status !== 'Cancelled' ? <><button disabled={busy} className="underline" onClick={() => { const branch = branches.find(b => b.id === request.branchId); if (branch) openRequest(branch, request); }}>Edit request</button><button disabled={busy} className="underline text-red-700" onClick={() => { if (confirm('Cancel this cleaning request?')) void act(() => partnerApi.cancel(request), 'Request cancelled.'); }}>Cancel request</button></> : request.status !== 'Cancelled' && <button className="underline" onClick={() => { setChange({ kind: 'request', id: request.id }); setMessage(''); }}>Request a change or cancellation</button>}</div></article>)}</section>}
      {tab === 'Change requests' && <section aria-label="Change request history" className="space-y-3">{!changes.length && <p>No change requests yet.</p>}{changes.map(item => <article key={item.id} className="bg-white border rounded p-4 space-y-2"><h2 className="font-semibold">{branches.find(branch => branch.id === item.branchId)?.name ?? 'Property'} · {item.kind}</h2><p>{item.resolvedUtc ? 'Resolved' : 'Awaiting staff response'}</p><p className="whitespace-pre-wrap">{item.message}</p>{item.resolvedUtc && <div><h3 className="font-semibold">Staff response</h3><p className="whitespace-pre-wrap">{item.resolution}</p></div>}</article>)}</section>}
      {tab === 'Company shifts' && <section className="space-y-3"><p>Existing company shifts are shown for reference. Contact staff to change them.</p>{!shifts.length && <p>No company shifts.</p>}{shifts.map(shift => <article key={shift.id} className="bg-white border rounded p-4"><h2 className="font-semibold">{shift.name}</h2><p>{shift.branchName} · {shift.isActive ? 'Active' : 'Inactive'} · {shift.validFrom ?? 'No start date'} – {shift.validUntil ?? 'Ongoing'}</p></article>)}</section>}
      {adding && <section aria-label="Add property" className="bg-white border rounded p-5"><h2 className="text-lg font-semibold">Add property</h2><form className="grid sm:grid-cols-2 gap-3" onSubmit={e => { e.preventDefault(); const data = new FormData(e.currentTarget); const body = Object.fromEntries(data); void act(async () => { await partnerApi.createBranch({ ...body, roomCount: body.roomCount === '' ? null : Number(body.roomCount), bedCount: body.bedCount === '' ? null : Number(body.bedCount) }); setAdding(false); }, 'Property created. Request a quote to enable cleaning requests.'); }}>{['name', 'street', 'postalCode', 'city', 'country', 'contactPhone', 'contactEmail', 'roomCount', 'bedCount'].map(field => <label key={field}>{({ name: 'Property name', postalCode: 'Postal code', contactPhone: 'Contact phone', contactEmail: 'Contact email', roomCount: 'Rooms (optional)', bedCount: 'Beds (optional)' } as Record<string, string>)[field] ?? field.charAt(0).toUpperCase() + field.slice(1)}<input className={input} name={field} required={field === 'name'} type={field.endsWith('Count') ? 'number' : field === 'contactEmail' ? 'email' : 'text'} min={0} step={1} /></label>)}<div className="flex gap-4"><button className={button} disabled={busy}>Save property</button><button type="button" disabled={busy} onClick={() => setAdding(false)}>Cancel</button></div></form></section>}
      {target && <section aria-label="Cleaning request form" className="bg-white border rounded p-5"><h2 className="text-lg font-semibold">{editing ? 'Edit' : 'New'} cleaning request · {target.name}</h2><p>One cleaning visit within your chosen dates. Times use {identity?.timeZoneId}.</p><form onSubmit={submitRequest} className="space-y-3"><label>From date<input className={input} type="date" required value={from} onChange={e => setFrom(e.target.value)} /></label><label className="flex gap-2"><input type="checkbox" checked={exact} onChange={e => setExact(e.target.checked)} />Fixed appointment on this date</label>{exact ? <label>Exact start time<input className={input} type="time" required value={time} onChange={e => setTime(e.target.value)} /></label> : <label>To date<input className={input} type="date" min={from} required value={to} onChange={e => setTo(e.target.value)} /></label>}<label>Cleaning instructions<textarea className={input} value={notes} maxLength={4000} onChange={e => setNotes(e.target.value)} /></label><div className="flex gap-4"><button className={button} disabled={busy}>Save cleaning request</button><button type="button" disabled={busy} onClick={() => setTarget(null)}>Cancel</button></div></form></section>}
      {change && <section aria-label="Request staff changes" className="bg-white border rounded p-5"><form onSubmit={e => { e.preventDefault(); void act(async () => { if (change.kind === 'branch') await partnerApi.branchChange(change.id, message); else await partnerApi.requestChange(change.id, message); setChange(null); }, 'Change request sent to staff.'); }}><label>Describe the requested change<textarea className={input} required maxLength={2000} value={message} onChange={e => setMessage(e.target.value)} /></label><div className="flex gap-4 mt-3"><button className={button} disabled={busy || !message.trim()}>Send change request</button><button type="button" disabled={busy} onClick={() => setChange(null)}>Cancel</button></div></form></section>}
    </>}
  </div></main>;
}
