'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import {
  Calendar,
  Clock,
  Play,
  CheckCircle2,
  Loader2,
  AlertTriangle,
  ChevronDown,
  ChevronUp,
  Upload,
} from 'lucide-react';
import { initAuth, isAuthenticated, authTenant, login, logout } from '@/lib/auth';
import { meApi, MyShiftOccurrence, QualityCycleFormItem, type ShiftCalendarView } from '@/lib/meApi';
import ShiftCalendar from './ShiftCalendar';
import { Logo } from '@/ds/Logo';
import { Button } from '@/ds/Button';
import { Card } from '@/ds/Card';
import { photoNoteMessage, readShiftPhotos, type ShiftPhoto } from '@/lib/shiftPhotos';

type Status = 'not-started' | 'in-progress' | 'submitted';

function statusOf(o: MyShiftOccurrence): Status {
  if (!o.form) return 'not-started';
  if (o.form.isSubmitted) return 'submitted';
  if (o.form.startedAtUtc) return 'in-progress';
  return 'not-started';
}

function statusLabel(s: Status): string {
  switch (s) {
    case 'not-started': return 'Not started';
    case 'in-progress': return 'In progress';
    case 'submitted': return 'Done';
  }
}

function statusClasses(s: Status): string {
  switch (s) {
    case 'not-started': return 'bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-300';
    case 'in-progress': return 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300';
    case 'submitted': return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300';
  }
}

function formatTenantDate(utc: string, timeZoneId: string, dateStyle: 'date' | 'time' | 'datetime') {
  const options: Intl.DateTimeFormatOptions = dateStyle === 'date'
    ? { year: 'numeric', month: 'short', day: 'numeric', timeZone: timeZoneId }
    : dateStyle === 'time'
      ? { hour: '2-digit', minute: '2-digit', timeZone: timeZoneId }
      : { dateStyle: 'medium', timeStyle: 'short', timeZone: timeZoneId };
  try { return new Intl.DateTimeFormat(undefined, options).format(new Date(utc)); }
  catch { return new Date(utc).toLocaleString(); }
}

function tenantDateKey(instant: string | Date, timeZoneId: string) {
  try {
    const parts = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: timeZoneId }).formatToParts(new Date(instant));
    return `${parts.find(p => p.type === 'year')?.value}-${parts.find(p => p.type === 'month')?.value}-${parts.find(p => p.type === 'day')?.value}`;
  } catch { return new Date(instant).toISOString().slice(0, 10); }
}

export default function MyShiftsPage() {
  const [ready, setReady] = useState(false);
  const [authed, setAuthed] = useState(false);
  const [occurrences, setOccurrences] = useState<MyShiftOccurrence[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const [view, setView] = useState<ShiftCalendarView>('today');
  const viewRef = useRef<ShiftCalendarView>('today');
  const [range, setRange] = useState<{ fromLocalDate: string; toLocalDate: string } | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const drafts = useRef<Record<string, { items: QualityCycleFormItem[]; notes: string; photos: ShiftPhoto[] }>>({});
  const loadVersion = useRef(0);
  const calendarZone = useRef('Europe/Helsinki');
  const calendarDay = useRef('');
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer); }, []);

  // Inline form state
  const [items, setItems] = useState<QualityCycleFormItem[]>([]);
  const [notes, setNotes] = useState('');
  const [photos, setPhotos] = useState<ShiftPhoto[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [photoReading, setPhotoReading] = useState(false);
  const [noteError, setNoteError] = useState(false);
  const noteRef = useRef<(HTMLTextAreaElement | null)[]>([]);

  useEffect(() => {
    if (!authed || !calendarDay.current || busy || photoReading || submitting) return;
    const day = tenantDateKey(new Date(now), calendarZone.current);
    if (day !== calendarDay.current) {
      calendarDay.current = day; closeForm(); void loadShifts();
    }
  }, [now, authed, busy, photoReading, submitting]);


  useEffect(() => {
    let active = true;
    initAuth().then((ok) => {
      if (!active) return;
      setReady(true);
      setAuthed(ok);
      if (!ok) return;
      loadShifts();
    });
    return () => { active = false; loadVersion.current++; };
  }, []);

  async function loadShifts(selected: ShiftCalendarView = viewRef.current, cursor?: string, append = false) {
    const version = ++loadVersion.current;
    if (append) setLoadingMore(true); else setLoading(true);
    setError(null);
    try {
      const data = await meApi.calendar(selected, cursor);
      if (version === loadVersion.current) {
        calendarZone.current = data.timeZoneId;
        calendarDay.current = tenantDateKey(new Date(), data.timeZoneId);
        setRange(data);
        setOccurrences(previous => append ? [...new Map([...previous, ...data.occurrences].map(o => [`${o.shiftId}-${o.startUtc}`, o])).values()] : data.occurrences);
      }
    } catch (failure) {
      if (version === loadVersion.current) setError(`Failed to load your shifts. ${failure instanceof Error ? failure.message : 'Please retry.'}`);
    } finally {
      if (version === loadVersion.current) { setLoading(false); setLoadingMore(false); }
    }
  }

  function closeForm() {
    if (openId) drafts.current[openId] = { items, notes, photos };
    setOpenId(null);
  }

  function changeView(selected: ShiftCalendarView) {
    if (busy || submitting || photoReading || selected === viewRef.current) return;
    closeForm(); setView(selected); viewRef.current = selected;
    setOccurrences([]); setRange(null); setLoadingMore(false);
    void loadShifts(selected);
  }

  async function handleStart(o: MyShiftOccurrence) {
    setBusy(true);
    setError(null);
    try {
      const form = await meApi.startShift(o.shiftId, o.startUtc, o.endUtc);
      if (form) {
        setOccurrences((prev) => prev.map((x) => (x.shiftId === o.shiftId && x.startUtc === o.startUtc ? { ...x, form } : x)));
      } else {
        if (!(await recoverSavedOccurrence(o, 'start'))) setError('Could not start shift. Please retry.');
      }
    } catch (error) {
      if (!(await recoverSavedOccurrence(o, 'start'))) setError(error instanceof Error && error.message !== 'Please retry.' ? `Could not start shift. ${error.message}` : 'Could not start shift. Please retry.');
    } finally {
      setBusy(false);
    }
  }

  async function recoverSavedOccurrence(o: MyShiftOccurrence, operation: 'start' | 'end'): Promise<boolean> {
    try {
      const latest = await meApi.myShifts(o.startUtc, new Date(Date.parse(o.startUtc) + 86400000).toISOString());
      const saved = latest.find(x => x.shiftId === o.shiftId && Date.parse(x.startUtc) === Date.parse(o.startUtc));
      if (!saved?.form || (operation === 'end' ? !saved.form.isSubmitted : !saved.form.startedAtUtc && !saved.form.isSubmitted)) return false;
      setOccurrences(current => current.map(x => x.shiftId === o.shiftId && Date.parse(x.startUtc) === Date.parse(o.startUtc) ? { ...x, form: saved.form } : x));
      if (operation === 'end' || saved.form.isSubmitted) { delete drafts.current[`${o.shiftId}-${o.startUtc}`]; setOpenId(null); }
      return true;
    } catch { return false; }
  }

  function openForm(o: MyShiftOccurrence) {
    if (photoReading || submitting) return;
    setNoteError(false);
    const key = `${o.shiftId}-${o.startUtc}`;
    if (openId) drafts.current[openId] = { items, notes, photos };
    setOpenId(openId === key ? null : key);
    const draft = drafts.current[key];
    if (draft) { setItems(draft.items); setNotes(draft.notes); setPhotos(draft.photos); return; }
    if (o.form) {
      setItems(o.form.items.map((i) => ({ ...i })));
      setNotes(o.form.cleanerNotes ?? '');
      setPhotos(o.form.photos?.length ? o.form.photos : (o.form.photoUrls ?? []).map(url => ({ url, note: '' })));
    }
  }

  function toggleItem(index: number) {
    setItems((prev) => prev.map((it, i) => (i === index ? { ...it, isChecked: !it.isChecked } : it)));
  }

  async function handlePhotoUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const files = Array.from(e.target.files ?? []);
    e.target.value = '';
    if (files.length === 0) return;
    setPhotoReading(true);
    setError(null);
    try { const uploaded = await readShiftPhotos(files, photos); setPhotos(prev => [...prev, ...uploaded.map(url => ({ url, note: '' }))]); }
    catch (error) { setError(error instanceof Error ? error.message : 'Could not read the photo. Please select it again.'); }
    finally { setPhotoReading(false); }
  }

  function handleRemovePhoto(index: number) {
    setPhotos((prev) => prev.filter((_, i) => i !== index));
  }

  async function handleEnd(o: MyShiftOccurrence) {
    if (!o.form || photoReading) return;
    const missing = photos.findIndex(p => !p.note.trim());
    if (missing >= 0) {
      setNoteError(true); setError(photoNoteMessage); noteRef.current[missing]?.focus(); return;
    }
    setNoteError(false);
    setSubmitting(true);
    setError(null);
    try {
      const result = await meApi.endForm(o.form.id, items, photos, notes);
      if (result) {
        setOccurrences((prev) => prev.map((x) => (x.shiftId === o.shiftId && x.startUtc === o.startUtc ? { ...x, form: result } : x)));
        setOpenId(null);
        delete drafts.current[`${o.shiftId}-${o.startUtc}`];
      } else {
        if (!(await recoverSavedOccurrence(o, 'end'))) setError('Could not end shift. Your selections are kept; please retry.');
      }
    } catch {
      if (!(await recoverSavedOccurrence(o, 'end'))) setError('Could not end shift. Your selections are kept; please retry.');
    } finally {
      setSubmitting(false);
    }
  }

  function renderOccurrence(o: MyShiftOccurrence) {
              const key = `${o.shiftId}-${o.startUtc}`;
              const status = statusOf(o);
              const isOpen = openId === key;
              const scheduledDate = tenantDateKey(o.startUtc, o.timeZoneId);
              const today = tenantDateKey(new Date(now), o.timeZoneId);
              const canStart = scheduledDate <= today;
              return (
                <Card key={key} role="group" aria-label={`${o.shiftName} ${o.startUtc}`} variant="elevated" padding="none" className="overflow-hidden">
                  <div className="p-5 space-y-3">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <h2 className="font-bold text-gray-900 dark:text-gray-100">{o.shiftName}</h2>
                        <div className="flex flex-wrap items-center gap-3 text-xs text-gray-500 dark:text-gray-400 pt-1">
                          <span className="flex items-center gap-1">
                            <Calendar className="w-3.5 h-3.5" />
                            Scheduled: {formatTenantDate(o.startUtc, o.timeZoneId, 'date')}
                          </span>
                          <span className="flex items-center gap-1">
                            <Clock className="w-3.5 h-3.5" />
                            {formatTenantDate(o.startUtc, o.timeZoneId, 'time')}
                            {' – '}
                            {formatTenantDate(o.endUtc, o.timeZoneId, 'time')}
                            {' · '}{o.timeZoneId}
                          </span>
                        </div>
                        {o.form?.startedAtUtc && <p className="text-xs text-gray-600 dark:text-gray-300 pt-1">Actual start: {formatTenantDate(o.form.startedAtUtc, o.timeZoneId, 'datetime')}</p>}
                        {o.form?.endedAtUtc && <p className="text-xs text-gray-600 dark:text-gray-300">Actual end: {formatTenantDate(o.form.endedAtUtc, o.timeZoneId, 'datetime')}</p>}
                      </div>
                      <span className={`text-xs font-semibold px-2.5 py-1 rounded-full ${statusClasses(status)}`}>
                        {statusLabel(status)}
                      </span>
                    </div>

                    {o.isDeleted && <p className="text-sm text-gray-500">Deleted shift · historical record</p>}
                    {status === 'not-started' && !o.isDeleted && (
                      <Button size="sm" onClick={() => handleStart(o)} disabled={busy || !canStart} className="gap-1.5">
                        <Play className="w-4 h-4" /> Start
                      </Button>
                    )}
                    {status === 'not-started' && !canStart && <p className="text-sm text-gray-500">Available on the scheduled date. The actual start time will be recorded.</p>}

                    {status === 'in-progress' && !o.isDeleted && (
                      <Button variant="outline" size="sm" disabled={photoReading || submitting} onClick={() => openForm(o)} className="gap-1.5">
                        {isOpen ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
                        Record activities & end
                      </Button>
                    )}

                    {status === 'submitted' && (
                      <div className="flex items-center gap-2 text-xs text-emerald-600 dark:text-emerald-400">
                        <CheckCircle2 className="w-4 h-4" />
                        Submitted {o.form?.submittedUtc ? formatTenantDate(o.form.submittedUtc, o.timeZoneId, 'datetime') : ''}
                      </div>
                    )}

                    {isOpen && status === 'in-progress' && !o.isDeleted && o.form && (
                      <div className="pt-3 border-t border-gray-100 dark:border-gray-700 space-y-4">
                        <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {o.form.templateTitle || 'Checklist'}
                        </h3>
                        <div className="space-y-2">
                          {items.map((item, index) => (
                            <label
                              key={index}
                              className={`flex items-center gap-2 p-3 rounded-lg border cursor-pointer transition-colors ${
                                item.isChecked
                                  ? 'bg-brand-50/60 border-brand-300 dark:bg-brand-950/30 dark:border-brand-800'
                                  : 'bg-white border-gray-200 hover:border-brand-200 dark:bg-gray-800 dark:border-gray-700'
                              }`}
                            >
                              <input type="checkbox" checked={item.isChecked} disabled={submitting} onChange={() => toggleItem(index)} className="accent-brand-500" />
                              <span className="text-sm text-gray-800 dark:text-gray-200">{item.itemText}</span>
                            </label>
                          ))}
                        </div>
                        {items.length === 0 && <p className="text-sm text-gray-600 dark:text-gray-300">No checklist assigned. You can end this shift without recording activities.</p>}
                        <textarea
                          aria-label="Shift note"
                          aria-required="false"
                          aria-describedby="shift-photo-note-help"
                          value={notes}
                          disabled={submitting}
                          onChange={(e) => setNotes(e.target.value)}
                          placeholder="Notes (optional)"
                          rows={2}
                          className="w-full rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 p-3 text-sm text-gray-800 dark:text-gray-200 focus:outline-none focus:ring-2 focus:ring-brand-400"
                        />
                        <p id="shift-photo-note-help" className="text-xs text-gray-600 dark:text-gray-300">Each photo needs its own note. General shift notes are optional, with or without photos.</p>
                        <div className="space-y-2">
                          {photos.length > 0 && (
                            <div className="flex flex-wrap gap-2">
                              {photos.map((p, i) => (
                                <div key={i} className="relative">
                                  <img src={p.url} alt={`photo-${i}`} className="w-16 h-16 object-cover rounded-lg border border-gray-200 dark:border-gray-700" />
                                  <label className="block text-sm">Photo {i + 1} note (required)
                                    <textarea ref={el => { noteRef.current[i] = el; }} aria-label={`Photo ${i + 1} note`} aria-required="true"
                                      aria-invalid={noteError && !p.note.trim()} value={p.note} disabled={submitting || photoReading}
                                      onChange={e => setPhotos(current => current.map((photo, index) => index === i ? { ...photo, note: e.target.value } : photo))}
                                      className="block w-full rounded border p-2 text-sm text-gray-900 bg-white" rows={2} />
                                  </label>
                                  <button
                                    type="button"
                                    disabled={submitting}
                                    onClick={() => handleRemovePhoto(i)}
                                    className="absolute -top-2 -right-2 w-5 h-5 rounded-full bg-red-500 text-white text-xs leading-none"
                                    aria-label="Remove photo"
                                  >
                                    ×
                                  </button>
                                </div>
                              ))}
                            </div>
                          )}
                          <label className="inline-flex items-center gap-1.5 text-xs font-medium text-brand-700 dark:text-brand-400 cursor-pointer">
                            <Upload className="w-4 h-4" />
                            Add photo
                            <input aria-label="Add shift photos" type="file" accept="image/png,image/jpeg,image/webp,image/bmp" multiple disabled={submitting || photoReading} className="hidden" onChange={handlePhotoUpload} />
                          </label>
                        </div>
                        {photoReading && <p role="status">Reading photos...</p>}
                        <Button size="sm" onClick={() => handleEnd(o)} disabled={submitting || photoReading} fullWidth className="gap-1.5">
                          {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <CheckCircle2 className="w-4 h-4" />}
                          End shift
                        </Button>
                      </div>
                    )}
                  </div>
                </Card>
              );

  }

  if (!ready) {
    return (
      <div className="min-h-screen flex items-center justify-center p-4">
        <Loader2 className="w-6 h-6 animate-spin text-brand-500" />
      </div>
    );
  }

  if (!authed) {
    return (
      <div className="min-h-screen bg-gradient-to-br from-accent-50 via-white to-brand-50 dark:from-gray-950 dark:to-gray-900 flex flex-col items-center justify-center p-4 gap-4">
        <Logo size="lg" />
        <p className="text-sm text-gray-600 dark:text-gray-300">Sign in to view your shifts.</p>
        <Button onClick={() => login()}>Sign in</Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-accent-50 via-white to-brand-50 dark:from-gray-950 dark:via-gray-950 dark:to-gray-900 py-8 px-4 sm:px-6">
      <div className="max-w-2xl mx-auto space-y-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <Logo size="md" />
            <h1 className="text-xl font-bold text-gray-900 dark:text-gray-100">My Shifts</h1>
          </div>
          <nav className="flex items-center gap-3">
            <Link className="text-sm underline text-gray-900 dark:text-gray-100" href={`/account/?tenant=${encodeURIComponent(authTenant())}`}>My account</Link>
            <Button variant="ghost" size="sm" onClick={() => logout()}>Sign out</Button>
          </nav>
        </div>

        {error && (
          <div role="alert" aria-label="Shift error" className="flex items-center gap-2 p-3 rounded-xl bg-red-50 border border-red-200 text-red-700 text-sm">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <div role="tablist" aria-label="Shift dates" className="flex gap-2">
          {(['past', 'today', 'future'] as const).map((tab, index, tabs) => <button
            key={tab} id={`shift-tab-${tab}`} type="button" role="tab" aria-selected={view === tab}
            aria-controls="my-shifts-panel" tabIndex={view === tab ? 0 : -1}
            disabled={busy || photoReading || submitting}
            className={`rounded-lg border px-4 py-2 font-semibold focus-visible:outline focus-visible:outline-2 ${view === tab ? 'bg-accent-900 text-white dark:bg-brand-400 dark:text-accent-950' : 'bg-white text-gray-800 dark:bg-gray-800 dark:text-gray-100'}`}
            onClick={() => changeView(tab)} onKeyDown={event => {
              let next = index;
              if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
              else if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
              else if (event.key === 'Home') next = 0;
              else if (event.key === 'End') next = tabs.length - 1;
              else return;
              event.preventDefault(); changeView(tabs[next]); document.getElementById(`shift-tab-${tabs[next]}`)?.focus();
            }}>{tab[0].toUpperCase() + tab.slice(1)}</button>)}
        </div>

        <div id="my-shifts-panel" role="tabpanel" aria-labelledby={`shift-tab-${view}`}>

        {loading ? (
          <div className="flex items-center justify-center py-16 text-gray-500">
            <Loader2 className="w-6 h-6 animate-spin" />
          </div>
        ) : error && occurrences.length === 0 ? (
          <Button onClick={() => loadShifts()}>Retry loading shifts</Button>
        ) : occurrences.length === 0 ? (
          <Card variant="elevated" padding="lg" className="text-center text-gray-500 dark:text-gray-400">
            No shifts scheduled.
          </Card>
        ) : (
          <div className="space-y-4">
            {view === 'today' ? occurrences.map(renderOccurrence) : <ShiftCalendar key={view} view={view} occurrences={occurrences} renderShift={renderOccurrence} disabled={busy || photoReading || submitting} onCloseForm={closeForm} />}
          </div>
        )}
        </div>
        {view !== 'today' && range && !loading && <Button variant="outline" disabled={loadingMore || busy || photoReading || submitting}
          onClick={() => void loadShifts(view, view === 'past' ? range.fromLocalDate : range.toLocalDate, true)}>
          {loadingMore ? 'Loading shifts…' : view === 'past' ? 'Load earlier shifts' : 'Load later shifts'}
        </Button>}
      </div>
    </div>
  );
}
