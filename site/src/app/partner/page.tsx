'use client';

import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type FormEvent, type KeyboardEvent } from 'react';
import Link from 'next/link';
import Image from 'next/image';
import { Building2, CalendarClock, KeyRound, MessageSquare, RefreshCw, Plus, ArrowRight, UserRound, LogOut, Settings } from 'lucide-react';
import { Badge, Button, buttonClasses, Card, Dialog, Field, Input, Textarea, fontFamily, palette } from '@/ds';
import { authTenant, initAuth, isAuthenticated, isPartner, isAdmin, isManager, login, logout } from '@/lib/auth';
import { partnerApi, type CleaningRequest, type PartnerAccount, type PartnerChangeAction, type PartnerCompletion, type PartnerIdentity, type PartnerShift, type PartnerChange } from '@/lib/partnerApi';
import { partnerCopy, partnerLanguageKey, partnerMessage, partnerStatus, type PartnerLanguage } from '@/lib/partnerLocale';
import type { Branch } from '@/lib/adminApi';
import { QuoteRequestedBadge } from '@/components/QuoteRequests';
import { PartnerInventoryAcknowledgments } from '@/components/PartnerInventoryAcknowledgments';
import { PartnerKeyRequests } from '@/components/PartnerKeyRequests';
import './partner.css';

const sections = [
  { key: 'Properties', label: 'properties', icon: Building2 },
  { key: 'Cleaning requests', label: 'requests', icon: CalendarClock },
  { key: 'Company shifts', label: 'shifts', icon: CalendarClock },
  { key: 'Change requests', label: 'changes', icon: MessageSquare },
  { key: 'Key requests', label: 'keys', icon: KeyRound },
  { key: 'Settings', label: 'settings', icon: Settings },
] as const;
type Section = typeof sections[number]['key'];
const theme = {
  '--partner-ink': palette.navy.logo, '--partner-muted': palette.navy[600],
  '--partner-background': palette.navy[50], '--partner-border': palette.navy[100],
  '--partner-gold': palette.gold[500], '--partner-gold-light': palette.gold[50],
  '--partner-focus': palette.gold[700],
  fontFamily: fontFamily.sans,
} as CSSProperties;
const base = partnerCopy.en;
// Identifies one settings state: photo sections and general instructions.
const settingsKey = (labels: string[], instructions: string, access: string) => JSON.stringify([labels, instructions, access]);

export default function PartnerPage() {
  const [language, setLanguage] = useState<PartnerLanguage>('en');
  const t = partnerCopy[language];
  const [identity, setIdentity] = useState<PartnerIdentity | null>(null), [branches, setBranches] = useState<Branch[]>([]), [requests, setRequests] = useState<CleaningRequest[]>([]), [shifts, setShifts] = useState<PartnerShift[]>([]);
  const [changes, setChanges] = useState<PartnerChange[]>([]);
  const [access, setAccess] = useState('pending'), [error, setError] = useState(''), [busy, setBusy] = useState(false), [notice, setNotice] = useState('');
  const [requestView, setRequestView] = useState<'active' | 'completed' | 'cancelled'>('active'), [requestPage, setRequestPage] = useState(1);
  const [tab, setTab] = useState<Section>('Properties'), [adding, setAdding] = useState(false), [editingProperty, setEditingProperty] = useState<Branch | null>(null), [target, setTarget] = useState<Branch | null>(null), [editing, setEditing] = useState<CleaningRequest | null>(null);
  // Property list filter: '' shows every city. Cities come from the properties themselves.
  const [cityFilter, setCityFilter] = useState('');
  // One-time visit setup: the photo sections every requested visit needs. Loaded with the portal data.
  const [photoLabels, setPhotoLabels] = useState<string[]>([]), [newPhotoLabel, setNewPhotoLabel] = useState('');
  // Saving is allowed only after the stored settings have loaded, so empty defaults never overwrite them.
  const [settingsReady, setSettingsReady] = useState(false), [settingsFailed, setSettingsFailed] = useState(false);
  // Last loaded or saved settings, and the current form. A reload applies the stored copy only when the form has no unsaved edits.
  const settingsSaved = useRef<string | null>(null), settingsDraft = useRef('');
  // General instructions the cleaner reads before every visit. Each visit's own instructions are appended after them.
  const [generalInstructions, setGeneralInstructions] = useState('');
  // Access instructions (key-safe or door codes) the cleaner sees before a visit. The rest of the stored account is kept as loaded.
  const [accessInstructions, setAccessInstructions] = useState(''), [account, setAccount] = useState<PartnerAccount>({});
  useEffect(() => { settingsDraft.current = settingsKey(photoLabels, generalInstructions, accessInstructions); }, [photoLabels, generalInstructions, accessInstructions]);
  const [from, setFrom] = useState(''), [to, setTo] = useState(''), [exact, setExact] = useState(false), [time, setTime] = useState(''), [notes, setNotes] = useState(''), [cleaningType, setCleaningType] = useState('Normal');
  const [change, setChange] = useState<{ kind: 'branch' | 'request'; id: string } | null>(null), [message, setMessage] = useState('');
  // Structured request changes: what the owner needs, and the proposed dates, time or instructions.
  const [changeAction, setChangeAction] = useState<PartnerChangeAction>('Message'), [changeFrom, setChangeFrom] = useState(''), [changeTo, setChangeTo] = useState(''), [changeTime, setChangeTime] = useState(''), [changeNotes, setChangeNotes] = useState('');
  function closeChange() { setChange(null); setChangeAction('Message'); setChangeFrom(''); setChangeTo(''); setChangeTime(''); setChangeNotes(''); }
  // The request whose cancellation is waiting for confirmation on its own card.
  const [cancelling, setCancelling] = useState<string | null>(null);
  const [cancelReason, setCancelReason] = useState('');
  // Completed-visit summary for one request, loaded when opened. Photos are large, so the list never carries them.
  const [visit, setVisit] = useState<{ request: CleaningRequest; data: PartnerCompletion | null; error: string } | null>(null);
  function openVisit(request: CleaningRequest) {
    setVisit({ request, data: null, error: '' });
    partnerApi.completion(request.id)
      .then(data => setVisit(current => current?.request.id === request.id ? { request, data, error: '' } : current))
      .catch((caught: Error) => setVisit(current => current?.request.id === request.id ? { request, data: null, error: caught.message || 'Request failed.' } : current));
  }
  const generation = useRef(0), requestKey = useRef('');
  // A state counter, so each page refresh is a real render the inventory panel can react to.
  const [inventoryRefresh, setInventoryRefresh] = useState(0);
  const tabsRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    let selected: PartnerLanguage = navigator.language.toLowerCase().startsWith('fi') ? 'fi' : 'en';
    try { const saved = localStorage.getItem(partnerLanguageKey); if (saved === 'fi' || saved === 'en') selected = saved; } catch { /* Storage is optional. */ }
    setLanguage(selected);
  }, []);
  function chooseLanguage(next: PartnerLanguage) {
    setLanguage(next);
    try { localStorage.setItem(partnerLanguageKey, next); } catch { /* The current page still switches. */ }
  }
  const refresh = useCallback(async () => {
    const attempt = ++generation.current; setError(''); setInventoryRefresh(value => value + 1);
    try {
      await initAuth(); if (attempt !== generation.current) return;
      if (!isAuthenticated()) { setAccess('anonymous'); return; }
      if (!isPartner() || isAdmin() || isManager()) { setAccess('denied'); return; }
      const person = await partnerApi.me();
      if (person.realm !== authTenant() || person.person.role !== 'partner' || !person.person.isActive || !person.company.isActive || !person.company.isPartnerEnabled) { setAccess('denied'); return; }
      const [properties, work, scheduled, changeHistory] = await Promise.all([partnerApi.branches(), partnerApi.requests(), partnerApi.shifts(), partnerApi.changes()]);
      if (attempt !== generation.current) return;
      setIdentity(person); setBranches(properties); setRequests(work); setShifts(scheduled); setChanges(changeHistory); setAccess('allowed');
      // Photo sections only shape new requests, so they load alongside the portal and never hold it up.
      partnerApi.settings()
        .then(photoSettings => {
          if (attempt !== generation.current) return;
          const labels = photoSettings.photoLabels ?? [], instructions = photoSettings.generalInstructions ?? '', stored = photoSettings.account ?? {};
          const access = stored.accessInstructions ?? '';
          // Unsaved edits win over a reload. Before any load or save, the form is compared with the empty defaults.
          if (settingsDraft.current === (settingsSaved.current ?? settingsKey([], '', ''))) {
            setPhotoLabels(labels); setGeneralInstructions(instructions); setAccessInstructions(access); setAccount(stored);
            settingsSaved.current = settingsDraft.current = settingsKey(labels, instructions, access);
          }
          setSettingsReady(true); setSettingsFailed(false);
        })
        .catch(() => { if (attempt === generation.current) { setSettingsReady(false); setSettingsFailed(true); } });
    } catch (e) { if (attempt === generation.current) { setError(e instanceof Error ? e.message : base.loadFailure); setAccess('error'); } }
  }, []);
  useEffect(() => { void refresh(); return () => { generation.current++; }; }, [refresh]);
  async function act(action: () => Promise<unknown>, success: string) {
    if (busy) return; setBusy(true); setError(''); setNotice('');
    try { await action(); setNotice(success); await refresh(); }
    catch (e) { setError(e instanceof Error ? e.message : base.operationFailed); }
    finally { setBusy(false); }
  }
  function openRequest(branch: Branch, request?: CleaningRequest) {
    setTarget({ ...branch }); setEditing(request ?? null); setFrom(request?.fromDate ?? ''); setTo(request?.toDate ?? '');
    setExact(Boolean(request?.exactStartLocalTime)); setTime(request?.exactStartLocalTime?.slice(0, 5) ?? '');
    setNotes(request?.notes ?? ''); setCleaningType(request?.cleaningType ?? 'Normal'); requestKey.current = crypto.randomUUID(); setError('');
  }
  async function submitRequest(e: FormEvent) {
    e.preventDefault(); if (!target) return;
    await act(async () => {
      const body = { fromDate: from, toDate: exact ? from : to || from, exactStartLocalTime: exact ? time : null, notes, cleaningType };
      if (editing) await partnerApi.updateRequest(editing.id, { ...body, version: editing.version });
      else await partnerApi.createRequest({ ...body, branchId: target.id, clientRequestId: requestKey.current });
      setTarget(null); setEditing(null); setTab('Cleaning requests');
    }, base.requestSaved);
  }
  function invalid(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const field = e.target as HTMLInputElement | HTMLTextAreaElement;
    setError(field.validity.rangeOverflow ? base.countRange : field.validity.rangeUnderflow ? (field.type === 'date' ? base.dateRange : base.nonNegative)
      : field.validity.typeMismatch ? base.invalidEmail : field.validity.stepMismatch ? base.invalidNumber : base.requiredFields);
    if (e.currentTarget.querySelector(':invalid') === field) field.focus();
  }
  function navigateTab(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const next = event.key === 'ArrowRight' ? (index + 1) % sections.length : event.key === 'ArrowLeft' ? (index + sections.length - 1) % sections.length : event.key === 'Home' ? 0 : event.key === 'End' ? sections.length - 1 : null;
    if (next === null) return;
    event.preventDefault(); setTab(sections[next].key);
    tabsRef.current?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus();
  }
  const locale = language === 'fi' ? 'fi-FI' : 'en-GB';
  const moneyFormatter = useMemo(() => new Intl.NumberFormat(locale, { style: 'currency', currency: 'EUR' }), [locale]);
  const numberFormatter = useMemo(() => new Intl.NumberFormat(locale), [locale]);
  const dateFormatter = useMemo(() => new Intl.DateTimeFormat(locale, { timeZone: 'UTC' }), [locale]);
  const stampFormatter = useMemo(() => new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone: identity?.timeZoneId }), [locale, identity?.timeZoneId]);
  const money = (value?: number | null) => value == null ? t.notSet : moneyFormatter.format(value);
  const number = (value: number) => numberFormatter.format(value);
  const date = (value: string) => language === 'fi' ? dateFormatter.format(new Date(value + 'T12:00:00Z')) : value;
  const stamp = (value: string | null) => value && !Number.isNaN(Date.parse(value))
    ? stampFormatter.format(new Date(value)) : t.notRecorded;
  const selectedIndex = sections.findIndex(section => section.key === tab);
  const counts = [branches.length, requests.length, shifts.length, changes.length, undefined, undefined];
  const cities = [...new Set(branches.map(branch => branch.city).filter((city): city is string => Boolean(city)))].sort((a, b) => a.localeCompare(b, locale));
  const activeCity = cities.includes(cityFilter) ? cityFilter : '';
  const shownBranches = activeCity ? branches.filter(branch => branch.city === activeCity) : branches;
  // Finished and cancelled requests move out of the active list. Both lists page 25 at a time.
  const activeRequests = requests.filter(request => !['Completed', 'Cancelled'].includes(request.status));
  const completedRequests = requests.filter(request => request.status === 'Completed');
  const cancelledRequests = requests.filter(request => request.status === 'Cancelled');
  const requestList = requestView === 'active' ? activeRequests : requestView === 'completed' ? completedRequests : cancelledRequests;
  const requestPageCount = Math.max(1, Math.ceil(requestList.length / 25));
  const currentRequestPage = Math.min(requestPage, requestPageCount);
  const pagedRequests = requestList.slice((currentRequestPage - 1) * 25, currentRequestPage * 25);
  const alert = error ? <p role="alert" className="partner-alert partner-alert-error">{partnerMessage(error, language)}</p> : null;

  return <main lang={language} className="partner-portal" style={theme}>
    <header className="partner-header"><div className="partner-shell partner-header-inner">
      <div className="partner-brand"><Image src="/brand/readyset-primary-logo.png" alt="ReadySetSiivous" width={56} height={56} priority /><div><h1>{t.portal}</h1><p>{identity?.company.name ?? t.intro}</p></div></div>
      <div className="partner-header-actions">
        <div className="partner-language" role="group" aria-label={t.language}>
          <button type="button" lang="fi" aria-label="Suomi" aria-pressed={language === 'fi'} onClick={() => chooseLanguage('fi')}>FI</button>
          <button type="button" lang="en" aria-label="English" aria-pressed={language === 'en'} onClick={() => chooseLanguage('en')}>EN</button>
        </div>
        <nav className="partner-account"><Link className={buttonClasses('ghost', 'sm') + ' partner-quiet'} href="/account/"><UserRound size={17} aria-hidden="true" />{t.account}</Link>
          {access !== 'anonymous' && <Button variant="ghost" size="sm" className="partner-quiet" onClick={() => logout()}><LogOut size={17} aria-hidden="true" />{t.signOut}</Button>}
        </nav>
      </div>
    </div></header>
    <div className="partner-shell partner-main">
      {access === 'pending' && <p role="status" className="partner-alert">{t.checking}</p>}
      {access === 'anonymous' && <Card className="partner-surface partner-welcome"><h2>{t.portal}</h2><p>{t.intro}</p><Button onClick={() => login()}>{t.signIn}<ArrowRight size={17} aria-hidden="true" /></Button></Card>}
      {access === 'denied' && <p role="alert" className="partner-alert partner-alert-error">{t.denied}</p>}
      {busy && <p role="status" className="partner-alert">{t.saving}</p>}
      {(!target || access !== 'allowed') && alert}
      {notice && <p role="status" className="partner-alert partner-alert-success">{partnerMessage(notice, language)}</p>}
      {['allowed', 'error'].includes(access) && <div className="partner-toolbar"><p>{t.timeZone} <strong>{identity?.timeZoneId ?? 'Europe/Helsinki'}</strong></p><Button variant="ghost" size="sm" className="partner-quiet" disabled={busy} onClick={() => void refresh()}><RefreshCw size={16} aria-hidden="true" />{t.refresh}</Button></div>}
      {access === 'allowed' && <>
        <PartnerInventoryAcknowledgments language={language} refreshToken={inventoryRefresh} />
        <div ref={tabsRef} role="tablist" aria-label={t.sections} className="partner-tabs">
          {sections.map((section, index) => <button key={section.key} id={'partner-tab-' + index} type="button" role="tab" aria-label={t[section.label]} aria-selected={tab === section.key} aria-controls="partner-section" tabIndex={tab === section.key ? 0 : -1} onKeyDown={event => navigateTab(event, index)} onClick={() => setTab(section.key)}>
            <section.icon size={18} aria-hidden="true" /><span>{t[section.label]}</span><span className="partner-count" aria-hidden="true">{counts[index]}</span>
          </button>)}
        </div>
        <div role="tabpanel" id="partner-section" aria-labelledby={'partner-tab-' + selectedIndex}>
          {tab === 'Properties' && <section className="partner-section">
            <div className="partner-section-heading"><div><h2>{t.properties}</h2><p>{t.propertiesHint}</p></div><Button variant="gold" disabled={busy} onClick={() => setAdding(true)}><Plus size={18} aria-hidden="true" />{t.addProperty}</Button></div>
            {!branches.length && <Card className="partner-surface partner-empty">{t.noProperties}</Card>}
            {branches.length > 0 && <div className="partner-filter"><label htmlFor="partner-city-filter" className="block mb-1.5 text-sm font-semibold">{t.filterCity}</label>
              <select id="partner-city-filter" aria-label={t.filterCity} className="partner-field partner-select" value={activeCity} onChange={event => setCityFilter(event.target.value)}>
                <option value="">{t.allCities}</option>
                {cities.map(city => <option key={city} value={city}>{city}</option>)}
              </select></div>}
            {branches.length > 0 && !shownBranches.length && <Card className="partner-surface partner-empty">{t.noPropertiesInCity}</Card>}
            <ul className="partner-property-list">{shownBranches.map(branch => <li key={branch.id}><article aria-label={branch.name}>
              <Card className="partner-surface partner-property">
                <div className="partner-property-identity">
                  <div className="partner-card-heading"><h3>{branch.name}</h3>{!branch.isActive ? <Badge variant="outline" className="partner-badge">{t.inactive}</Badge> : branch.pricingReady ? <Badge variant="subtle" className="partner-badge">{t.ready}</Badge> : null}</div>
                  <QuoteRequestedBadge branch={branch} label={t.quoteRequested} />
                  {branch.quoteRejectedUtc && !branch.quoteRequestedUtc && !branch.pricingReady && <p role="status" className="partner-muted">{t.quoteRejected}: {branch.quoteRejectionReason}</p>}
                  <p className="partner-muted">{[branch.street, branch.postalCode, branch.city].filter(Boolean).join(', ')}</p>
                </div>
                <dl className="partner-property-facts"><div><dt>{t.rooms}</dt><dd>{branch.roomCount ?? t.notSet}</dd></div><div><dt>{t.beds}</dt><dd>{branch.bedCount ?? t.notSet}</dd></div></dl>
                <div className="partner-price">{branch.pricingReady ? <><p><strong>{money(branch.priceNet)}</strong> {t.exclVat} ({t.vat} {number(branch.vatRatePercent ?? 0)}%)</p><p className="partner-muted">{t.estimated} {branch.estimatedDurationMinutes} {t.minutes}</p></> : <p>{branch.quoteRequestedUtc ? t.quoteWaiting : t.pricingRequired}</p>}</div>
                <div className="partner-card-actions"><Button disabled={busy || !branch.isActive || !branch.pricingReady} onClick={() => openRequest(branch)}>{t.requestCleaning}<ArrowRight size={16} aria-hidden="true" /></Button>
                  {!branch.pricingReady && <Button variant="outline" className="partner-secondary" disabled={busy || !branch.isActive || Boolean(branch.quoteRequestedUtc)} onClick={() => act(() => partnerApi.quote(branch.id), base.quoteSaved)}>{t.requestQuote}</Button>}
                  {!branch.pricingReady && branch.quoteRequestedUtc && <Button variant="outline" className="partner-secondary" disabled={busy} onClick={() => act(() => partnerApi.withdrawQuote(branch.id), base.quoteWithdrawn)}>{t.withdrawQuote}</Button>}
                  <Button variant="outline" className="partner-secondary" disabled={busy || !branch.isActive} onClick={() => setEditingProperty(branch)}>{t.editProperty}</Button>
                  <Button variant="ghost" size="sm" className="partner-quiet" onClick={() => { setChange({ kind: 'branch', id: branch.id }); setMessage(''); }}>{t.propertyChanges}</Button>
                </div>
              </Card>
            </article></li>)}</ul>
          </section>}
          {tab === 'Cleaning requests' && <section className="partner-section">
            <div className="partner-section-heading"><div><h2>{t.requests}</h2><p>{t.requestsHint}</p></div></div>
            <div className="partner-form-actions" role="tablist" aria-label={t.requests}><Button role="tab" aria-selected={requestView === 'active'} variant={requestView === 'active' ? 'primary' : 'outline'} className={requestView === 'active' ? '' : 'partner-secondary'} onClick={() => { setRequestView('active'); setRequestPage(1); }}>{t.activeRequests} ({activeRequests.length})</Button><Button role="tab" aria-selected={requestView === 'completed'} variant={requestView === 'completed' ? 'primary' : 'outline'} className={requestView === 'completed' ? '' : 'partner-secondary'} onClick={() => { setRequestView('completed'); setRequestPage(1); }}>{t.completedRequests} ({completedRequests.length})</Button><Button role="tab" aria-selected={requestView === 'cancelled'} variant={requestView === 'cancelled' ? 'primary' : 'outline'} className={requestView === 'cancelled' ? '' : 'partner-secondary'} onClick={() => { setRequestView('cancelled'); setRequestPage(1); }}>{t.cancelledRequests} ({cancelledRequests.length})</Button></div>
            {!requestList.length && <Card className="partner-surface partner-empty">{t.noRequests}</Card>}
            <div className="partner-grid">{pagedRequests.map(request => <article key={request.id} aria-label={request.branchName + ' ' + request.fromDate}>
              <Card className="partner-surface partner-request">
                <div className="partner-card-heading"><h3>{request.branchName}<span className="sr-only"> · {partnerStatus(request.status, language)}</span></h3>
                  <Badge variant={request.status === 'Completed' ? 'subtle' : 'outline'} className="partner-badge">{partnerStatus(request.status, language)}</Badge>
                </div>
                <p className="partner-request-window">{date(request.fromDate)} – {date(request.toDate)}{request.exactStartLocalTime && ' ' + t.at + ' ' + request.exactStartLocalTime.slice(0, 5)}</p>
                <p className="partner-price">{t.agreedPrice} <strong>{money(request.priceNet)}</strong> {t.exclVat} ({t.vat} {number(request.vatRatePercent)}%)</p>
                <p className="partner-muted">{t.cleaningType}: {request.cleaningType === 'Deep' ? t.deepCleaning : t.normalCleaning}</p>
                <div className="partner-request-details"><p>{t.planned}: {request.plannedStartUtc ? <>{stamp(request.plannedStartUtc)} – {stamp(request.plannedEndUtc)}</> : request.firstAcceptedAtUtc || request.status === 'Accepted' ? t.flexible : t.notRecorded}</p><p className="partner-actual-times">{t.actualStart}: {stamp(request.actualStartUtc)} · {t.actualEnd}: {stamp(request.actualEndUtc)}</p></div>
                {request.notes && <p className="partner-entered">{request.notes}</p>}
                <div className="partner-card-actions">{request.status === 'Completed' ? <Button variant="outline" className="partner-secondary" onClick={() => openVisit(request)}>{t.viewVisit}</Button> : !request.firstAcceptedAtUtc && request.status !== 'Cancelled' ? <>
                  <Button variant="outline" className="partner-secondary" disabled={busy} onClick={() => { const branch = branches.find(item => item.id === request.branchId); if (branch) openRequest(branch, request); }}>{t.editRequest}</Button>
                  <Button variant="ghost" className="partner-danger" disabled={busy} onClick={() => { setCancelReason(''); setCancelling(request.id); }}>{t.cancelRequest}</Button>
                </> : request.status !== 'Cancelled' && <Button variant="outline" className="partner-secondary" onClick={() => { setChange({ kind: 'request', id: request.id }); setMessage(''); }}>{t.changeOrCancel}</Button>}</div>
                {cancelling === request.id && <div role="group" aria-label={t.cancelConfirm} className="partner-confirm"><p>{t.cancelConfirm}</p><Field label={t.cancelReason} className="partner-form-wide"><Textarea className="partner-field" required maxLength={1000} value={cancelReason} onChange={event => setCancelReason(event.target.value)} /></Field><div className="partner-card-actions"><Button variant="ghost" className="partner-danger" disabled={busy || !cancelReason.trim()} onClick={() => { const reason = cancelReason.trim(); void act(async () => { try { await partnerApi.cancel(request, reason); } catch (error) { await refresh(); throw error; } setCancelling(null); setCancelReason(''); }, base.requestCancelled); }}>{t.confirmCancel}</Button><Button variant="outline" className="partner-secondary" disabled={busy} onClick={() => { setCancelling(null); setCancelReason(''); }}>{t.keepRequest}</Button></div></div>}
              </Card>
            </article>)}</div>
            <div className="partner-form-actions" role="navigation" aria-label={requestView === 'active' ? t.activeRequests : requestView === 'completed' ? t.completedRequests : t.cancelledRequests}>{requestList.length > 25 && <><Button variant="outline" className="partner-secondary" disabled={currentRequestPage <= 1} onClick={() => setRequestPage(currentRequestPage - 1)}>{t.previousPage}</Button><span className="partner-muted">{currentRequestPage} / {requestPageCount}</span><Button variant="outline" className="partner-secondary" disabled={currentRequestPage >= requestPageCount} onClick={() => setRequestPage(currentRequestPage + 1)}>{t.nextPage}</Button></>}</div>
          </section>}
          {tab === 'Key requests' && <section aria-label={t.keyRequests} className="partner-section">
            <PartnerKeyRequests t={t} language={language} branches={branches} money={money} date={date} />
          </section>}
          {tab === 'Change requests' && <section aria-label={t.history} className="partner-section">
            <div className="partner-section-heading"><div><h2>{t.changes}</h2><p>{t.changesHint}</p></div></div>
            {!changes.length && <Card className="partner-surface partner-empty">{t.noChanges}</Card>}
            <div className="partner-grid">{changes.map(item => <article key={item.id}><Card className="partner-surface">
              <h3>{branches.find(branch => branch.id === item.branchId)?.name ?? t.property} · {item.kind === 'Property' ? t.kindProperty : item.kind === 'Request' || item.kind === 'CleaningRequest' ? t.kindRequest : t.kindChange}</h3>
              <Badge variant="subtle" className="partner-badge">{item.resolvedUtc ? t.resolved : t.awaitingStaff}</Badge>
              <p className="partner-entered">{item.message}</p>
              {item.action && item.action !== 'Message' && <p className="partner-requested-change"><strong>{t.requestedChange}:</strong>{' '}
                {item.action === 'Reschedule' ? `${item.proposedFromDate ? date(item.proposedFromDate) : ''} – ${item.proposedToDate ? date(item.proposedToDate) : ''}${item.proposedExactStartLocalTime ? ` ${t.at} ${item.proposedExactStartLocalTime.slice(0, 5)}` : ''}`
                  : item.action === 'Instructions' ? item.proposedNotes : t.kindCancel}</p>}
              {item.resolvedUtc && <div className="partner-staff-response"><h4>{t.staffResponse}</h4><p className="partner-entered">{item.resolution}</p></div>}
            </Card></article>)}</div>
          </section>}
          {tab === 'Settings' && <section className="partner-section">
            <div className="partner-section-heading"><div><h2>{t.visitPhotoSettings}</h2><p>{t.visitPhotoSettingsHint}</p></div></div>
            <Card className="partner-surface partner-settings">
              <form className="partner-form-grid" onSubmit={event => { event.preventDefault(); if (!settingsReady) return; void act(async () => { const saved = await partnerApi.saveSettings({ photoLabels, generalInstructions, account: { ...account, accessInstructions: accessInstructions.trim() || null } }); const savedAccess = saved.account?.accessInstructions ?? ''; setPhotoLabels(saved.photoLabels); setGeneralInstructions(saved.generalInstructions ?? ''); setAccessInstructions(savedAccess); setAccount(saved.account ?? {}); settingsSaved.current = settingsDraft.current = settingsKey(saved.photoLabels, saved.generalInstructions ?? '', savedAccess); }, base.photoSectionsSaved); }}>
                {settingsFailed && <p role="alert" className="partner-alert partner-alert-error partner-form-wide">{t.settingsLoadFailed}</p>}
                <div className="partner-form-wide">
                  {!photoLabels.length && <p className="partner-muted">{t.noPhotoSections}</p>}
                  <ul className="partner-photo-labels">{photoLabels.map((label, index) => <li key={label}><span>{label}</span><Button variant="ghost" size="sm" className="partner-quiet" disabled={busy} aria-label={`Remove ${label}`} onClick={() => setPhotoLabels(current => current.filter((_, position) => position !== index))}>{t.removePhotoSection}</Button></li>)}</ul>
                </div>
                <div className="partner-form-wide partner-photo-suggestions">{['Before', 'After', 'Keys', 'Entrance'].filter(suggestion => !photoLabels.includes(suggestion)).map(suggestion => <Button key={suggestion} type="button" variant="outline" className="partner-secondary" disabled={busy || photoLabels.length >= 10} aria-label={`Add ${suggestion}`} onClick={() => setPhotoLabels(current => [...current, suggestion])}>{suggestion}</Button>)}</div>
                <Field label={t.photoSectionName} className="partner-form-wide"><Input className="partner-field" value={newPhotoLabel} maxLength={60} onChange={event => setNewPhotoLabel(event.target.value)} /></Field>
                <p className="partner-muted partner-form-wide">{t.generalInstructionsHint}</p>
                <Field label={t.generalInstructions} className="partner-form-wide"><Textarea className="partner-field" maxLength={2000} value={generalInstructions} onChange={event => setGeneralInstructions(event.target.value)} /></Field>
                <p className="partner-muted partner-form-wide">{t.accessInstructionsHint}</p>
                <Field label={t.accessInstructions} className="partner-form-wide"><Textarea className="partner-field" maxLength={2000} value={accessInstructions} onChange={event => setAccessInstructions(event.target.value)} /></Field>
                <div className="partner-form-actions">
                  <Button type="button" variant="outline" className="partner-secondary" disabled={busy || !newPhotoLabel.trim() || photoLabels.length >= 10 || photoLabels.some(label => label.toLowerCase() === newPhotoLabel.trim().toLowerCase())} onClick={() => { setPhotoLabels(current => [...current, newPhotoLabel.trim()]); setNewPhotoLabel(''); }}>{t.addPhotoSection}</Button>
                  <Button type="submit" disabled={busy || !settingsReady}>{t.savePhotoSections}</Button>
                </div>
              </form>
            </Card>
          </section>}
          {tab === 'Company shifts' && <section className="partner-section">
            <div className="partner-section-heading"><div><h2>{t.shifts}</h2><p>{t.shiftsHint}</p></div></div>
            {!shifts.length && <Card className="partner-surface partner-empty">{t.noShifts}</Card>}
            <div className="partner-grid">{shifts.map(shift => <article key={shift.id}><Card className="partner-surface"><h3>{shift.name}</h3><p>{shift.branchName}</p><Badge variant="outline" className="partner-badge">{shift.isActive ? t.active : t.inactive}</Badge><p>{shift.validFrom ? date(shift.validFrom) : t.noStart} – {shift.validUntil ? date(shift.validUntil) : t.ongoing}</p></Card></article>)}</div>
          </section>}
        </div>
        <Dialog open={adding || editingProperty !== null} title={editingProperty ? t.editProperty : t.addProperty} onClose={() => { setAdding(false); setEditingProperty(null); }} className="partner-form-panel">
          <Card className="partner-surface">
            <form key={editingProperty?.id ?? 'new'} className="partner-form-grid" onInvalid={invalid} onSubmit={event => {
              event.preventDefault(); const fields = Object.fromEntries(new FormData(event.currentTarget));
              const body = { ...fields, roomCount: fields.roomCount === '' ? null : Number(fields.roomCount), bedCount: fields.bedCount === '' ? null : Number(fields.bedCount) };
              void act(async () => { if (editingProperty) { await partnerApi.updateBranch(editingProperty.id, body); setEditingProperty(null); } else { await partnerApi.createBranch(body); setAdding(false); } }, editingProperty ? base.propertyUpdated : base.propertyCreated);
            }}>
              {([
                ['name', t.propertyName], ['street', t.street], ['postalCode', t.postalCode], ['city', t.city], ['country', t.country],
                ['contactPhone', t.phone], ['contactEmail', t.email], ['roomCount', t.optionalRooms], ['bedCount', t.optionalBeds],
              ] as const).map(([name, label]) => <Field key={name} label={label}><Input className="partner-field" name={name} defaultValue={editingProperty ? String((editingProperty as unknown as Record<string, unknown>)[name] ?? '') : undefined} required={name === 'name'} maxLength={name === 'name' ? 200 : undefined} type={name.endsWith('Count') ? 'number' : name === 'contactEmail' ? 'email' : 'text'} min={0} max={name.endsWith('Count') ? 10000 : undefined} step={1} /></Field>)}
              <div className="partner-form-actions"><Button type="submit" disabled={busy}>{t.saveProperty}</Button><Button variant="outline" className="partner-secondary" disabled={busy} onClick={() => { setAdding(false); setEditingProperty(null); }}>{t.cancel}</Button></div>
            </form>
          </Card>
        </Dialog>
        <Dialog open={target !== null} title={`${editing ? t.editCleaning : t.newCleaning} · ${target?.name ?? ""}`} onClose={() => setTarget(null)} className="partner-form-panel">
          <Card className="partner-surface">{alert}<p className="partner-muted">{t.oneVisit} {t.timesUse} {identity?.timeZoneId}.</p>
          {photoLabels.length > 0 && <p className="partner-muted">{t.photosRequired}: {photoLabels.join(', ')}</p>}
          {generalInstructions.trim() && <p className="partner-muted">{t.generalInstructionsApplied}</p>}
            <form onSubmit={submitRequest} onInvalid={invalid} className="partner-form-grid">
              <Field label={t.fromDate}><Input className="partner-field" name="fromDate" aria-describedby="partner-window-help" type="date" required value={from} onChange={event => setFrom(event.target.value)} /></Field>
              {exact ? <Field label={t.exactTime}><Input className="partner-field" name="exactTime" type="time" required value={time} onChange={event => setTime(event.target.value)} /></Field> : <Field label={t.toDate}><Input className="partner-field" name="toDate" aria-describedby="partner-window-help" type="date" min={from} required value={to} onChange={event => setTo(event.target.value)} /></Field>}
              <p className="partner-muted partner-form-wide" id="partner-window-help">{t.windowHint}</p>
              <label className="partner-check"><input type="checkbox" checked={exact} onChange={event => setExact(event.target.checked)} />{t.fixedAppointment}</label>
              <p className="partner-muted partner-form-wide">{t.visitInstructionsHint}</p>
              <Field label={t.cleaningType}><select className="partner-field" name="cleaningType" aria-label={t.cleaningType} value={cleaningType} onChange={event => setCleaningType(event.target.value)}><option value="Normal">{t.normalCleaning}</option><option value="Deep">{t.deepCleaning}</option></select>{cleaningType === 'Deep' && <span className="partner-muted" role="note">{t.deepSurchargeNote}</span>}</Field>
              <Field label={t.instructions} className="partner-form-wide"><Textarea className="partner-field" value={notes} maxLength={4000} onChange={event => setNotes(event.target.value)} /></Field>
              <div className="partner-form-actions"><Button type="submit" disabled={busy}>{t.saveRequest}</Button><Button variant="outline" className="partner-secondary" disabled={busy} onClick={() => setTarget(null)}>{t.cancel}</Button></div>
            </form>
          </Card>
        </Dialog>
        <Dialog open={visit !== null} title={t.visitTitle} onClose={() => setVisit(null)} className="partner-form-panel">
          {visit && <Card className="partner-surface partner-visit">
            {visit.error && <p role="alert" className="partner-alert partner-alert-error">{partnerMessage(visit.error, language)}</p>}
            {!visit.error && !visit.data && <p role="status" className="partner-muted">{t.visitLoading}</p>}
            {visit.data && <>
              <h2 className="partner-visit-title">{visit.data.branchName}</h2>
              <p className="partner-muted">{date(visit.data.fromDate)} – {date(visit.data.toDate)}</p>
              <dl className="partner-property-facts">
                <div><dt>{t.actualStart}</dt><dd>{stamp(visit.data.actualStartUtc)}</dd></div>
                <div><dt>{t.actualEnd}</dt><dd>{stamp(visit.data.actualEndUtc)}</dd></div>
              </dl>
              {!visit.data.reports.length && <p className="partner-muted">{t.noTasks}</p>}
              {visit.data.reports.map((report, index) => <section key={index} aria-label={report.templateTitle} className="partner-visit-report">
                <h3>{report.templateTitle}</h3>
                <h4>{t.visitTasks}</h4>
                {report.tasks.length ? <>
                  <p className="partner-muted">{t.tasksDone}: {report.tasks.filter(task => task.done).length} / {report.tasks.length}</p>
                  <ul className="partner-visit-tasks">{report.tasks.map((task, taskIndex) => <li key={taskIndex}><label><input type="checkbox" checked={task.done} disabled /> {task.text}</label></li>)}</ul>
                </> : <p className="partner-muted">{t.noTasks}</p>}
                <h4>{t.visitNotes}</h4>
                {report.cleanerNotes ? <p className="partner-entered">{report.cleanerNotes}</p> : <p className="partner-muted">{t.noNotes}</p>}
                <h4>{t.visitPhotos}</h4>
                {report.photos.length ? <div className="partner-visit-photos">{report.photos.map((photo, photoIndex) => <figure key={photoIndex}><img src={photo.url} alt={`${t.photo} ${photoIndex + 1}: ${photo.note || t.noPhotoNote}`} /><figcaption>{photo.note || t.noPhotoNote}</figcaption></figure>)}</div> : <p className="partner-muted">{t.noPhotos}</p>}
              </section>)}
              <h3>{t.visitMessages}</h3>
              {!visit.data.messages.length && <p className="partner-muted">{t.noVisitMessages}</p>}
              <ul className="partner-visit-messages">{visit.data.messages.map(item => <li key={item.id}><p className="partner-entered">{item.message}</p>{item.resolvedUtc && <p className="partner-staff-response"><strong>{t.staffResponse}:</strong> {item.resolution}</p>}</li>)}</ul>
            </>}
            <div className="partner-form-actions"><Button variant="outline" className="partner-secondary" onClick={() => setVisit(null)}>{t.closeVisit}</Button></div>
          </Card>}
        </Dialog>
        <Dialog open={change !== null} title={t.staffChanges} onClose={closeChange} className="partner-form-panel">
          <Card className="partner-surface"><form onInvalid={invalid} className="partner-form-grid" onSubmit={event => {
            event.preventDefault(); void act(async () => { if (change?.kind === 'branch') await partnerApi.branchChange(change?.id ?? "", message); else await partnerApi.requestChange(change?.id ?? "", { message, action: changeAction, fromDate: changeFrom || undefined, toDate: changeTo || undefined, exactStartLocalTime: changeTime || null, notes: changeAction === 'Instructions' ? changeNotes : null }); closeChange(); }, base.changeSent);
          }}>
            {change?.kind === 'request' && <>
              <div className="partner-form-wide"><label htmlFor="partner-change-kind" className="block mb-1.5 text-sm font-semibold">{t.changeKind}</label>
                <select id="partner-change-kind" className="partner-field partner-select" value={changeAction} onChange={event => setChangeAction(event.target.value as PartnerChangeAction)}>
                  <option value="Message">{t.kindMessage}</option><option value="Reschedule">{t.kindReschedule}</option><option value="Instructions">{t.kindInstructions}</option><option value="Cancel">{t.kindCancel}</option>
                </select></div>
              {changeAction === 'Reschedule' && <>
                <Field label={t.newFromDate}><Input className="partner-field" type="date" required value={changeFrom} onChange={event => setChangeFrom(event.target.value)} /></Field>
                <Field label={t.newToDate}><Input className="partner-field" type="date" required min={changeFrom} value={changeTo} onChange={event => setChangeTo(event.target.value)} /></Field>
                <Field label={t.newTime}><Input className="partner-field" type="time" value={changeTime} onChange={event => setChangeTime(event.target.value)} /></Field>
              </>}
              {changeAction === 'Instructions' && <Field label={t.newInstructions} className="partner-form-wide"><Textarea className="partner-field" required maxLength={4000} value={changeNotes} onChange={event => setChangeNotes(event.target.value)} /></Field>}
            </>}
            <Field label={t.describeChange} className="partner-form-wide"><Textarea className="partner-field" required maxLength={2000} value={message} onChange={event => setMessage(event.target.value)} /></Field>
            <div className="partner-form-actions"><Button type="submit" disabled={busy || !message.trim()}>{t.sendChange}</Button><Button variant="outline" className="partner-secondary" disabled={busy} onClick={closeChange}>{t.cancel}</Button></div>
          </form></Card>
        </Dialog>
      </>}
    </div>
  </main>;
}
