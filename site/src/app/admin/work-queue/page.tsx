'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { Loader } from '@mantine/core';
import { adminCompanies, adminEmployees, adminTenant, adminWorkQueue, type Employee, type WorkQueueItem } from '@/lib/adminApi';
import { staffPartnerApi } from '@/lib/partnerApi';
import { showError, showSuccess } from '@/lib/toast';
import { SOURCES, SOURCE_CHIP, SOURCE_LABEL, TYPES, TYPE_LABEL, buildRows, type Kind, type Row, type Source, type Sources } from './model';
import { WorkQueueDrawer } from './WorkQueueDrawer';
import './work-queue.css';

const PAGE_SIZE = 25;
const DEFAULT_ZONE = 'Europe/Helsinki';
type SourceFilter = 'all' | Source;
type TypeFilter = 'all' | Kind;
type Sort = 'asc' | 'desc';
const SOURCE_VALUES = SOURCES.map(s => s.value);
const TYPE_VALUES = TYPES.map(t => t.value);

/** Every unassigned visit or booking of one source, read page by page (the server serves at most 100 a page). */
async function unassigned(source: 'booking' | 'shift'): Promise<WorkQueueItem[]> {
  const items: WorkQueueItem[] = [];
  for (let skip = 0; skip < 2000; skip += 100) {
    const page = await adminWorkQueue.page({ view: 'needs-action', source, assignee: 'unassigned', skip, take: 100 });
    if (!page) throw new Error('The work queue could not be loaded.');
    items.push(...page.items);
    if (page.items.length < 100 || items.length >= page.total) break;
  }
  return items;
}

export default function WorkQueuePage() {
  const [data, setData] = useState<Sources | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [source, setSource] = useState<SourceFilter>('all');
  const [type, setType] = useState<TypeFilter>('all');
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState<Sort>('asc');
  const [page, setPage] = useState(1);
  const [openId, setOpenId] = useState<string | null>(null);
  const [ready, setReady] = useState(false);
  const opener = useRef<HTMLElement | null>(null);
  const generation = useRef(0);

  const load = useCallback(async () => {
    const version = ++generation.current;
    setLoading(true); setLoadError('');
    const api = staffPartnerApi('admin');
    const [tenant, shifts, bookings, requests, quotes, changes, keys, branches, employees, companies] = await Promise.allSettled([
      adminTenant.get(), unassigned('shift'), unassigned('booking'), api.requests(), api.quotes(), api.changes(), api.keyRequests(), api.branches(),
      adminEmployees.list(), adminCompanies.listAll(),
    ]);
    if (version !== generation.current) return;
    const value = <T,>(result: PromiseSettledResult<T>, fallback: T): T => result.status === 'fulfilled' && result.value != null ? result.value : fallback;
    const failed = [shifts, bookings].some(r => r.status === 'rejected');
    const partnerFailed = [requests, quotes, changes, keys].some(r => r.status === 'rejected');
    // Show whatever loaded; only when nothing at all could be read is there nothing to show.
    if ([shifts, bookings, requests, quotes, changes, keys].every(r => r.status === 'rejected')) { setLoadError('The work queue could not be loaded.'); setLoading(false); return; }
    if (failed) showError('Unassigned visits and bookings could not be loaded. Partner work is shown.', 'Work queue incomplete');
    if (partnerFailed) showError('Partner requests could not be loaded. Visits and bookings are shown.', 'Work queue incomplete');
    // Partner work counts only for companies with partner access, as on Partner operations.
    const companyList = value(companies, []) ?? [];
    const partnerCompanies = new Set(companyList.filter(c => c.isPartnerEnabled).map(c => c.id));
    const forPartners = <T extends { companyId: string }>(items: T[]) => companies.status === 'fulfilled' && companies.value ? items.filter(item => partnerCompanies.has(item.companyId)) : items;
    const allRequests = forPartners(value(requests, []));
    const people = (value(employees, [] as Employee[]) ?? []).filter(e => e.isActive && e.role !== 'partner');
    setData({
      zone: value(tenant, null as { timeZoneId?: string } | null)?.timeZoneId?.trim() || DEFAULT_ZONE,
      shifts: value(shifts, []), bookings: value(bookings, []),
      requests: allRequests.filter(r => r.status === 'Requested' || r.status === 'Offered'), allRequests,
      quotes: forPartners(value(quotes, [])).filter(b => b.isActive && !!b.quoteRequestedUtc && !b.pricingReady),
      changes: forPartners(value(changes, [])).filter(c => !c.resolvedUtc),
      keys: forPartners(Array.isArray(value(keys, null)?.items) ? value(keys, null)!.items : []).filter(k => k.status !== 'Completed' && k.status !== 'Cancelled'),
      companies: new Map(companyList.map(c => [c.id, c.name])),
      branches: new Map(value(branches, []).map(b => [b.id, b])),
      employees: people,
    });
    setLoading(false);
  }, []);

  // Filters live in the URL, so a link or Back opens the same queue.
  const readUrl = () => {
    const params = new URLSearchParams(window.location.search);
    const s = params.get('source') as SourceFilter | null, t = params.get('type') as TypeFilter | null, n = Number(params.get('page'));
    setSource(s && SOURCE_VALUES.includes(s) ? s : 'all');
    setType(t && TYPE_VALUES.includes(t) ? t : 'all');
    setQuery(params.get('q') ?? '');
    setSort(params.get('sort') === 'desc' ? 'desc' : 'asc');
    setPage(Number.isInteger(n) && n >= 1 ? n : 1);
    const booking = params.get('booking');
    if (booking) setOpenId(`booking-${booking}`);
  };
  const writeUrl = (next: { source: SourceFilter; type: TypeFilter; q: string; sort: Sort; page: number }) => {
    try {
      const params = new URLSearchParams();
      if (next.source !== 'all') params.set('source', next.source);
      if (next.type !== 'all') params.set('type', next.type);
      if (next.q.trim()) params.set('q', next.q.trim());
      if (next.sort !== 'asc') params.set('sort', next.sort);
      if (next.page > 1) params.set('page', String(next.page));
      const qs = params.toString();
      window.history.replaceState(null, '', `${window.location.pathname}${qs ? `?${qs}` : ''}`);
    } catch { /* the queue works without URL state */ }
  };
  useEffect(() => {
    readUrl(); setReady(true); void load();
    const onFocus = () => { if (document.visibilityState === 'visible') void load(); };
    window.addEventListener('popstate', readUrl);
    document.addEventListener('visibilitychange', onFocus);
    return () => { window.removeEventListener('popstate', readUrl); document.removeEventListener('visibilitychange', onFocus); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  useEffect(() => { if (ready) writeUrl({ source, type, q: query, sort, page }); }, [ready, source, type, query, sort, page]);

  const rows = useMemo(() => (data ? buildRows(data) : []), [data]);
  const q = query.trim().toLowerCase();
  const list = useMemo(() => rows
    .filter(row => (source === 'all' || row.source === source) && (type === 'all' || row.kind === type)
      && (!q || `${row.customer} ${row.reference}`.toLowerCase().includes(q)))
    .sort((a, b) => (sort === 'asc' ? 1 : -1) * (a.sortKey.localeCompare(b.sortKey) || a.customer.localeCompare(b.customer) || a.id.localeCompare(b.id))), [rows, source, type, q, sort]);
  const pageCount = Math.max(1, Math.ceil(list.length / PAGE_SIZE));
  const current = Math.min(page, pageCount);
  const pageRows = list.slice((current - 1) * PAGE_SIZE, current * PAGE_SIZE);
  const overdue = list.filter(row => row.alert).length;
  const open = openId ? rows.find(row => row.id === openId) ?? null : null;

  const openRow = (row: Row, target: HTMLElement) => { opener.current = target; setOpenId(row.id); };
  const closeDrawer = useCallback(() => {
    setOpenId(null);
    try { const params = new URLSearchParams(window.location.search); if (params.has('booking')) { params.delete('booking'); window.history.replaceState(null, '', `${window.location.pathname}${params.size ? `?${params}` : ''}`); } } catch { /* ignore */ }
    const target = opener.current; opener.current = null;
    if (target?.isConnected) window.setTimeout(() => target.focus(), 0);
  }, []);
  const done = useCallback((message: string) => { showSuccess(message); closeDrawer(); void load(); }, [closeDrawer, load]);
  const failed = useCallback((message: string) => { showError(message, 'Not saved'); }, []);

  const filter = (patch: Partial<{ source: SourceFilter; type: TypeFilter; q: string; sort: Sort }>) => {
    if (patch.source) setSource(patch.source);
    if (patch.type) setType(patch.type);
    if (patch.q !== undefined) setQuery(patch.q);
    if (patch.sort) setSort(patch.sort);
    setPage(1);
  };

  return (
    <div className="work-queue">
      <header>
        <h1>Work queue</h1>
        <p className="lede">Everything waiting for the office: visits without an assignee, partner cleaning requests, quote requests, change requests and key requests. Assigned visits are in the <Link href="/admin/shifts/scheduler/">Scheduler</Link>.</p>
      </header>

      <div className="filters">
        <label className="field">Source<select className="input" value={source} onChange={event => filter({ source: event.target.value as SourceFilter })}>{SOURCES.map(s => <option key={s.value} value={s.value}>{s.label}</option>)}</select></label>
        <label className="field">Type<select className="input" value={type} onChange={event => filter({ type: event.target.value as TypeFilter })}>{TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}</select></label>
        <label className="field">Search<input className="input" type="search" placeholder="Customer, property or reference" value={query} onChange={event => filter({ q: event.target.value })} /></label>
        <label className="field">Sort<select className="input" value={sort} onChange={event => filter({ sort: event.target.value as Sort })}><option value="asc">Date, earliest first</option><option value="desc">Date, latest first</option></select></label>
      </div>

      {loading && !data ? <Loader aria-label="Loading the work queue" /> : loadError ? (
        <div className="row"><p role="alert" className="problem">{loadError}</p><button type="button" className="btn btn-ghost" onClick={() => void load()}>Retry</button></div>
      ) : <>
        <strong role="status">{list.length === 0 ? 'Nothing is waiting.' : `${list.length} ${list.length === 1 ? 'item is' : 'items are'} waiting${overdue ? `, ${overdue} overdue.` : '.'}`}</strong>
        {pageRows.length === 0 && <p className="note">{q ? `Nothing waiting matches “${query.trim()}”.` : 'New visits, bookings and partner requests appear here when they need the office.'}</p>}
        <div role="list" aria-label="Work waiting for the office" className="list">
          {pageRows.map(row => (
            <div role="listitem" key={row.id} className={`item${row.alert ? ' alert' : ''}`}>
              <div className="item-text">
                <strong>{row.customer}</strong>
                <span className="item-sub">{row.reference} · {row.when}</span>
                <span className="tags">
                  <span className={SOURCE_CHIP[row.source]}>{SOURCE_LABEL[row.source]}</span>
                  <span className="chip chip-type">{TYPE_LABEL[row.kind]}</span>
                  {row.cleanType && <span className="chip chip-sub">{row.cleanType}</span>}
                  <span className={`chip ${row.alert ? 'chip-alert' : 'chip-state'}`}>{row.state}</span>
                </span>
              </div>
              <button type="button" className="btn btn-primary" aria-label={`${row.action} ${TYPE_LABEL[row.kind].toLowerCase()} ${row.reference}`} onClick={event => openRow(row, event.currentTarget)}>{row.action}</button>
            </div>
          ))}
        </div>
        <div className="pager">
          <span>{list.length ? `Showing ${(current - 1) * PAGE_SIZE + 1}–${Math.min(list.length, current * PAGE_SIZE)} of ${list.length}` : 'Showing 0 of 0'}</span>
          <div className="row"><button type="button" className="btn btn-ghost" disabled={current <= 1} onClick={() => setPage(current - 1)}>Previous</button><span>Page {current} of {pageCount}</span><button type="button" className="btn btn-ghost" disabled={current >= pageCount} onClick={() => setPage(current + 1)}>Next</button></div>
        </div>
      </>}

      {open && data && <WorkQueueDrawer key={open.id} row={open} employees={data.employees} zone={data.zone} onClose={closeDrawer} onDone={done} onFail={failed} />}
    </div>
  );
}
