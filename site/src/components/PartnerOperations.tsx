'use client';
import { Suspense, useCallback, useEffect, useRef, useState, type FormEvent, type ButtonHTMLAttributes } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import '@mantine/core/styles.css';
import './partner-operations.css';
import { Button as MantineButton, Input, NativeSelect, Textarea, Paper, Title, MantineProvider } from '@mantine/core';
import { adminTheme } from '@/lib/adminTheme';
import { adminCompanies, adminEmployees, type Branch } from '@/lib/adminApi';
import { managerApi } from '@/lib/managerApi';
import { staffPartnerApi, type CleaningRequest, type PartnerChange } from '@/lib/partnerApi';
import { QuoteRequestedBadge, useQuoteRequests } from '@/components/QuoteRequests';

const input = 'partner-staff-input';
const button = 'partner-staff-primary';
function StaffButton({ className, type = 'submit', ...props }: ButtonHTMLAttributes<HTMLButtonElement>) {
  return <MantineButton {...props} type={type} variant={props.role === 'tab' ? (props['aria-selected'] ? 'light' : 'subtle') : className === button ? 'filled' : 'subtle'} />;
}
interface Person { autoAcceptWork?: boolean; id: string; firstName: string; lastName: string; isActive: boolean; role?: string }
function revealEditor(editor: HTMLElement | null) {
  editor?.focus({ preventScroll: true });
  editor?.scrollIntoView({ block: 'start', behavior: 'instant' });
}
export function PartnerOperations({ role }: { role: 'admin' | 'manager' }) {
  const content = <Suspense fallback={<p role="status">Loading partner operations…</p>}><PartnerOperationsContent role={role} /></Suspense>;
  return role === 'manager' ? <MantineProvider theme={adminTheme} forceColorScheme="light">{content}</MantineProvider> : content;
}
function PartnerOperationsContent({ role }: { role: 'admin' | 'manager' }) {
  const searchParams = useSearchParams();
  const router = useRouter();
  const quoteRequests = useQuoteRequests();
  const [requests, setRequests] = useState<CleaningRequest[]>([]), [quotes, setQuotes] = useState<Branch[]>([]), [changes, setChanges] = useState<PartnerChange[]>([]), [branches, setBranches] = useState<Branch[]>([]);
  const [people, setPeople] = useState<Person[]>([]), [companies, setCompanies] = useState<{ id: string; name: string; isActive: boolean; isPartnerEnabled?: boolean }[]>([]);
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [error, setError] = useState(''), [notice, setNotice] = useState(''), [tab, setTab] = useState('Requests');
  const [offer, setOffer] = useState<CleaningRequest | null>(null), [employee, setEmployee] = useState(''), [start, setStart] = useState('');
  const fixedStart = Boolean(offer?.exactStartLocalTime && offer.fromDate === offer.toDate);
  const automaticCleaner = people.find(person => person.id === employee)?.autoAcceptWork === true;
  const [pricing, setPricing] = useState<Branch | null>(null), [price, setPrice] = useState(''), [vat, setVat] = useState('25.5'), [duration, setDuration] = useState('');
  const [property, setProperty] = useState<Branch | 'new' | null>(null), [resolution, setResolution] = useState<PartnerChange | null>(null), [text, setText] = useState('');
  const [editing, setEditing] = useState<CleaningRequest | null>(null);
  const propertyEditor = useRef<HTMLElement>(null), pricingEditor = useRef<HTMLElement>(null);
  useEffect(() => { if (property) revealEditor(propertyEditor.current); }, [property]);
  useEffect(() => { if (pricing) revealEditor(pricingEditor.current); }, [pricing]);
  const load = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const api = staffPartnerApi(role);
      const [work, pricingQueue, changeQueue, properties, roster, clients] = await Promise.all([api.requests(), api.quotes(), api.changes(), api.branches(), role === 'admin' ? adminEmployees.list() : managerApi.employees(), role === 'admin' ? adminCompanies.listAll() : managerApi.companies()]);
      if (!roster || !clients) throw new Error('Company or employee data could not be loaded.');
      const partnerCompanies = clients.filter(company => company.isPartnerEnabled);
      const partnerCompanyIds = new Set(partnerCompanies.map(company => company.id));
      setRequests(work); setQuotes(pricingQueue.filter(branch => partnerCompanyIds.has(branch.companyId))); setChanges(changeQueue); setBranches(properties.filter(branch => partnerCompanyIds.has(branch.companyId))); setPeople(roster.filter(person => person.isActive && (!('role' in person) || person.role !== 'partner'))); setCompanies(partnerCompanies.filter(company => company.isActive));
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not load partner operations.'); }
    finally { setLoading(false); }
  }, [role]);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => { if (searchParams.get('tab') === 'quotes') setTab('Quote requests'); }, [searchParams]);
  function selectTab(name: string) {
    setTab(name);
    const query = new URLSearchParams(searchParams.toString());
    if (name === 'Quote requests') query.set('tab', 'quotes'); else query.delete('tab');
    router.replace(window.location.pathname + (query.size ? '?' + query.toString() : ''), { scroll: false });
  }
  async function act(work: () => Promise<unknown>, success: string) {
    if (busy) return; setBusy(true); setError(''); setNotice('');
    try { const result = await work(); setNotice(typeof result === 'string' ? result : success); await load(); await quoteRequests?.refresh(); } catch (e) { setError(e instanceof Error ? e.message : 'Change could not be confirmed. Refresh before retrying.'); } finally { setBusy(false); }
  }
  function openPrice(branch: Branch) { if (pricing === branch) revealEditor(pricingEditor.current); setPricing(branch); setPrice(branch.priceNet?.toString() ?? ''); setVat(branch.vatRatePercent?.toString() ?? '25.5'); setDuration(branch.estimatedDurationMinutes?.toString() ?? ''); }
  function openProperty(branch: Branch | 'new') {
    if (property === branch) revealEditor(propertyEditor.current);
    setProperty(branch);
  }
  function saveProperty(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); const fields = Object.fromEntries(new FormData(e.currentTarget));
    const record = property !== 'new' ? property : null;
    const body = { ...fields, companyId: record?.companyId ?? fields.companyId, isActive: record?.isActive ?? true, roomCount: fields.roomCount === '' ? null : Number(fields.roomCount), bedCount: fields.bedCount === '' ? null : Number(fields.bedCount), priceNet: record?.priceNet ?? null, vatRatePercent: record?.vatRatePercent ?? null, estimatedDurationMinutes: record?.estimatedDurationMinutes ?? null };
    void act(async () => { const api = staffPartnerApi(role); if (record) await api.updateBranch(record.id, body); else await api.createBranch(body); setProperty(null); }, 'Property saved.');
  }
  const visibleRequests = requests.filter(request => request.status !== 'Cancelled');
  const pendingCounts: Record<string, number> = {
    Requests: quoteRequests?.requestCount ?? requests.filter(request => request.status === 'Requested').length,
    'Quote requests': quoteRequests?.branches.length ?? quotes.filter(branch => branch.isActive && !branch.pricingReady).length,
    Changes: quoteRequests?.changeCount ?? changes.filter(change => !change.resolvedUtc).length,
  };
  return <section aria-busy={busy || loading} className="partner-staff space-y-5"><header className="flex flex-wrap justify-between gap-4"><Title order={1}>Partner operations</Title><StaffButton disabled={busy || loading} className="underline" onClick={() => { void load(); void quoteRequests?.refresh(); }}>Refresh partner operations</StaffButton></header>
    <p>Price properties, arrange one-time cleaning and handle owner change requests. Times entered below use the tenant time zone.</p>
    {busy && <p role="status">Saving changes…</p>}{error && <p role="alert" className="text-red-700">{error}</p>}{notice && <p role="status">{notice}</p>}{loading && <p role="status">Loading partner operations…</p>}
    <div className="flex flex-wrap gap-3" role="tablist" aria-label="Partner operations sections">{['Requests', 'Quote requests', 'Changes', 'Properties'].map(name => <StaffButton role="tab" key={name} aria-selected={tab === name} className={tab === name ? button : 'border rounded px-4 py-2'} onClick={() => selectTab(name)} aria-label={name}>{name}{pendingCounts[name] > 0 && <span aria-label={`${pendingCounts[name]} pending`} className="partner-staff-count">{pendingCounts[name]}</span>}</StaffButton>)}</div>
    {!loading && tab === 'Requests' && <div className="space-y-3">{!visibleRequests.length && <p>No partner cleaning requests.</p>}{visibleRequests.map(request => <Paper component="article" withBorder radius="md" p="md" className="border rounded bg-white p-4 space-y-2" key={request.id} aria-label={`${request.branchName} request`}><h2 className="font-semibold">{request.branchName} · {request.status}</h2><p>{request.fromDate} – {request.toDate}{request.exactStartLocalTime && ` at ${request.exactStartLocalTime}`}</p><p>€{request.priceNet.toFixed(2)} excl. VAT ({request.vatRatePercent}%) · {request.estimatedDurationMinutes} minutes</p><p>Cleaner: {request.acceptedCleanerName ?? 'Not accepted'}</p>{request.notes && <p className="whitespace-pre-wrap">{request.notes}</p>}{!['Completed', 'InProgress', 'Cancelled'].includes(request.status) && <div className="flex flex-wrap gap-3"><StaffButton className={button} disabled={busy} onClick={() => { setOffer(request); setEmployee(request.offeredEmployeeId ?? ''); setStart(''); }}>Arrange cleaner / time</StaffButton><StaffButton className="underline" disabled={busy} onClick={() => setEditing(request)}>Edit / reschedule request</StaffButton><StaffButton className="underline" disabled={busy} onClick={() => { const reason = prompt('Reason for cancellation'); if (reason?.trim()) void act(() => staffPartnerApi(role).cancel(request, reason), 'Request cancelled.'); }}>Cancel request</StaffButton></div>}</Paper>)}</div>}
    {!loading && tab === 'Quote requests' && <div className="space-y-3">{!quotes.length && <p>No quotes awaiting pricing.</p>}{quotes.map(branch => <Paper component="article" withBorder radius="md" p="md" className="border bg-white rounded p-4" key={branch.id}><h2 className="font-semibold">{branch.name}</h2><QuoteRequestedBadge branch={branch} /><p>{companies.find(company => company.id === branch.companyId)?.name ?? branch.companyId} · {branch.city}</p><p>Rooms: {branch.roomCount ?? 'Not set'} · Beds: {branch.bedCount ?? 'Not set'}</p><StaffButton className={button} disabled={busy} onClick={() => openPrice(branch)}>Set cleaning price</StaffButton></Paper>)}</div>}
    {!loading && tab === 'Changes' && <div className="space-y-3">{!changes.length && <p>No change requests.</p>}{changes.map(change => <Paper component="article" withBorder radius="md" p="md" className="border bg-white rounded p-4 space-y-2" key={change.id}><h2 className="font-semibold">{change.kind} · {companies.find(company => company.id === change.companyId)?.name ?? change.companyId}</h2><p>{branches.find(branch => branch.id === change.branchId)?.name ?? ''}</p><p className="whitespace-pre-wrap">{change.message}</p><p>{change.resolvedUtc ? `Resolved: ${change.resolution}` : 'Awaiting staff response'}</p>{!change.resolvedUtc && <StaffButton className={button} disabled={busy} onClick={() => { setResolution(change); setText(''); }}>Resolve change request</StaffButton>}</Paper>)}</div>}
    {!loading && tab === 'Properties' && <div className="space-y-3"><StaffButton className={button} disabled={busy} onClick={() => openProperty('new')}>Add property</StaffButton>{!branches.length && <p>No partner properties.</p>}{branches.map(branch => <Paper component="article" withBorder radius="md" p="md" key={branch.id} className="border rounded bg-white p-4 space-y-2"><h2 className="font-semibold">{branch.name} · {companies.find(company => company.id === branch.companyId)?.name}</h2><QuoteRequestedBadge branch={branch} /><p>{branch.street}, {branch.city} · Rooms: {branch.roomCount ?? 'Not set'} · Beds: {branch.bedCount ?? 'Not set'}</p><p>{branch.pricingReady ? `€${branch.priceNet?.toFixed(2)} excl. VAT · ${branch.estimatedDurationMinutes} minutes` : 'Pricing not ready'}</p><div className="flex flex-wrap gap-3"><StaffButton className="underline" disabled={busy} onClick={() => openProperty(branch)}>Edit property</StaffButton><StaffButton className="underline" disabled={busy} onClick={() => openPrice(branch)}>Set cleaning price</StaffButton></div></Paper>)}</div>}
    {offer && <section className="border rounded bg-white p-5" aria-label="Arrange cleaning"><h2 className="font-semibold">Arrange {offer.branchName}</h2><form className="space-y-3" onSubmit={e => { e.preventDefault(); void act(async () => { const result = await staffPartnerApi(role).offer(offer, employee, start); setOffer(null); return result.status === 'Accepted' ? 'Work scheduled automatically.' : 'Offer sent. Waiting for cleaner acceptance.'; }, 'Offer sent. Waiting for cleaner acceptance.'); }}><p>Window: {offer.fromDate} – {offer.toDate}. {offer.exactStartLocalTime ? `Fixed start ${offer.exactStartLocalTime}.` : automaticCleaner ? 'Set the exact planned start before sending this offer.' : 'Leave time blank to let the cleaner choose when accepting.'}</p><label>Cleaner<NativeSelect aria-label="Cleaner" required className={input} value={employee} onChange={e => setEmployee(e.target.value)}><option value="">Select cleaner</option>{people.map(person => <option key={person.id} value={person.id}>{person.firstName} {person.lastName}{person.autoAcceptWork ? ' · Automatic acceptance' : ''}</option>)}</NativeSelect></label><p>{automaticCleaner ? 'This cleaner automatically accepts offers. An exact planned start is required.' : 'This cleaner accepts offers manually.'}</p><label>Planned start (tenant local time{automaticCleaner && !fixedStart ? ' — required' : ', optional'})<Input required={automaticCleaner && !fixedStart} className={input} type="datetime-local" min={`${offer.fromDate}T00:00`} max={`${offer.toDate}T23:59`} value={start} onChange={e => setStart(e.target.value)} /></label><div className="flex flex-wrap gap-3"><StaffButton className={button} disabled={busy}>Send cleaning offer</StaffButton><StaffButton type="button" disabled={busy} onClick={() => setOffer(null)}>Cancel</StaffButton></div></form></section>}
    {pricing && <section ref={pricingEditor} tabIndex={-1} className="border rounded bg-white p-5 scroll-mt-4" aria-label="Property pricing"><h2 className="font-semibold">Price {pricing.name}</h2><p>New requests use the new price. Existing requests retain their agreed price.</p><form className="space-y-3" onSubmit={e => { e.preventDefault(); void act(async () => { await staffPartnerApi(role).price(pricing.id, { priceNet: Number(price), vatRatePercent: Number(vat), estimatedDurationMinutes: Number(duration) }); setPricing(null); }, 'Pricing saved.'); }}><label>Fixed cleaning price excl. VAT (€)<Input className={input} required type="number" min="0" step="0.01" value={price} onChange={e => setPrice(e.target.value)} /></label><label>VAT (%)<Input className={input} required type="number" min="0" max="100" step="0.01" value={vat} onChange={e => setVat(e.target.value)} /></label><label>Estimated duration (minutes)<Input className={input} required type="number" min="1" step="1" value={duration} onChange={e => setDuration(e.target.value)} /></label><div className="flex flex-wrap gap-3"><StaffButton className={button} disabled={busy}>Save pricing</StaffButton><StaffButton type="button" disabled={busy} onClick={() => setPricing(null)}>Cancel</StaffButton></div></form></section>}
    {resolution && <form className="border rounded bg-white p-5 space-y-3" onSubmit={e => { e.preventDefault(); void act(async () => { await staffPartnerApi(role).resolve(resolution.id, text); setResolution(null); }, 'Change request resolved.'); }}><label>Resolution<Textarea className={input} required value={text} onChange={e => setText(e.target.value)} /></label><StaffButton className={button} disabled={busy || !text.trim()}>Save resolution</StaffButton><StaffButton type="button" className="ml-4" disabled={busy} onClick={() => setResolution(null)}>Cancel</StaffButton></form>}
    {editing && <section key={editing.id} aria-label="Edit cleaning request" className="border rounded bg-white p-5"><h2 className="font-semibold">Edit / reschedule {editing.branchName}</h2><p>Changes invalidate outstanding offers and require renewed cleaner acceptance. Started or billed work is protected.</p><form className="space-y-3" onSubmit={event => { event.preventDefault(); const data = new FormData(event.currentTarget); void act(async () => { await staffPartnerApi(role).update(editing, { fromDate: String(data.get('fromDate')), toDate: String(data.get('toDate')), exactStartLocalTime: String(data.get('exactStartLocalTime')) || null, notes: String(data.get('notes')) }); setEditing(null); }, 'Request updated. Arrange a new cleaner offer.'); }}><label>From date<Input name="fromDate" type="date" className={input} required defaultValue={editing.fromDate} /></label><label>To date<Input name="toDate" type="date" className={input} required defaultValue={editing.toDate} /></label><label>Exact start time (optional; single date only)<Input name="exactStartLocalTime" type="time" className={input} defaultValue={editing.exactStartLocalTime?.slice(0, 5) ?? ''} /></label><label>Cleaning instructions<Textarea className={input} name="notes" defaultValue={editing.notes ?? ''} /></label><div className="flex flex-wrap gap-3"><StaffButton className={button} disabled={busy}>Save request changes</StaffButton><StaffButton type="button" disabled={busy} onClick={() => setEditing(null)}>Cancel</StaffButton></div></form></section>}
    {property && <section ref={propertyEditor} tabIndex={-1} aria-label="Property editor" key={property === 'new' ? 'new' : property.id} className="border rounded bg-white p-5 scroll-mt-4"><h2 className="font-semibold">{property === 'new' ? 'Add' : 'Edit'} property</h2><form onSubmit={saveProperty} className="grid sm:grid-cols-2 gap-3">{property === 'new' && <label>Company<NativeSelect name="companyId" className={input} required><option value="">Select company</option>{companies.map(company => <option value={company.id} key={company.id}>{company.name}</option>)}</NativeSelect></label>}{['name', 'street', 'postalCode', 'city', 'country', 'contactPhone', 'contactEmail', 'roomCount', 'bedCount'].map(field => <label key={field}>{({ name: 'Property name', postalCode: 'Postal code', contactPhone: 'Contact phone', contactEmail: 'Contact email', roomCount: 'Rooms (optional)', bedCount: 'Beds (optional)' } as Record<string, string>)[field] ?? field}<Input name={field} className={input} required={field === 'name'} type={field.endsWith('Count') ? 'number' : field === 'contactEmail' ? 'email' : 'text'} min={0} step={1} defaultValue={property === 'new' ? '' : String(property[field as keyof Branch] ?? '')} /></label>)}<div className="flex flex-wrap gap-3"><StaffButton className={button} disabled={busy}>Save property</StaffButton><StaffButton type="button" disabled={busy} onClick={() => setProperty(null)}>Cancel</StaffButton></div></form></section>}
  </section>;
}
