'use client';

import { useEffect, useMemo, useRef, useState, type CSSProperties, type DragEvent, type ReactNode } from 'react';
import { Loader } from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import { adminBranchVisits, adminEmployees, adminShifts, adminTenant, authorizedFetch, type Employee, type EmployeeDaySchedule, type OverlapFinding, type ScheduleVisit, type Shift } from '@/lib/adminApi';
import { calendarDuration, calendarLocalInput, calendarRequest, CalendarAccessError, type AdminShiftCalendar, type CalendarEmployee, type CalendarVisit } from '@/lib/adminShiftCalendarApi';
import { readShiftPhotos, photoNoteMessage, type ShiftPhoto } from '@/lib/shiftPhotos';
import './scheduler.css';

// The Scheduler joins the employee schedule (who works when, drag to move or reassign) and the
// shift calendar (actual work times, review flags, photos). Its layout follows docs/design/office-portal/Scheduler.dc.html.
const DAYS = 7;
const HOUR_PX = 56;
const SNAP_MINUTES = 15;
const MOVE_REASON = 'Moved in the scheduler';
const RESIZE_REASON = 'End time changed in the scheduler';
const ASSIGN_REASON = 'Reassigned in the scheduler';
const UNDO_REASON = 'Undo in the scheduler';
const MODE_REPLACE = 1;
const STATUS_MOVED = 1;
const STATUS_EXTRA = 4;
const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const LENGTHS = [[30, '0 h 30 min'], [60, '1 h'], [90, '1 h 30 min'], [120, '2 h'], [150, '2 h 30 min'], [180, '3 h'], [240, '4 h'], [300, '5 h'], [360, '6 h'], [480, '8 h']] as const;

type Kind = 'done' | 'progress' | 'sched' | 'needs' | 'cancelled' | 'offered';
const KIND: Record<Kind, { text: string; block: string; chip: string; locked: boolean }> = {
  done: { text: 'Done', block: 'blk-done', chip: 'chip chip-done', locked: true },
  progress: { text: 'In progress', block: 'blk-progress', chip: 'chip chip-progress', locked: true },
  sched: { text: 'Scheduled', block: 'blk-sched', chip: 'chip chip-sched', locked: false },
  needs: { text: 'Needs action', block: 'blk-needs', chip: 'chip chip-needs', locked: false },
  cancelled: { text: 'Cancelled', block: 'blk-cancelled', chip: 'chip chip-mute', locked: true },
  // Offered to the employee, who has not accepted yet: greyed, and it can only be offered to someone else.
  offered: { text: 'Waiting for acceptance', block: 'blk-offered', chip: 'chip chip-mute', locked: true },
};
const kindOf = (status?: string | null): Kind =>
  status === 'Offered' ? 'offered' : status === 'Completed' ? 'done' : status === 'InProgress' ? 'progress' : status === 'NeedsAction' ? 'needs' : 'sched';

type Block = {
  key: string; employeeId: string; day: string; start: number; end: number; kind: Kind; overlap: boolean;
  shiftId: string; name: string; startUtc: string; endUtc: string; place: string;
  visit: ScheduleVisit | null; calVisit: CalendarVisit | null; person: CalendarEmployee | null;
};
type Column = { key: string; name: string; sub: string; hours: string; employeeId: string; day: string; blocks: Block[] };
type Drag = { block: Block; grab: number; resize: boolean };

function isoDate(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
function addDays(day: string, count: number) {
  const [y, m, d] = day.split('-').map(Number);
  return isoDate(new Date(y, m - 1, d + count));
}
function mondayOf(day: string) {
  const [y, m, d] = day.split('-').map(Number);
  const weekday = new Date(y, m - 1, d).getDay();
  return addDays(day, weekday === 0 ? -6 : 1 - weekday);
}
const weekdayOf = (day: string) => WEEKDAYS[new Date(`${day}T12:00:00Z`).getUTCDay()];
const isDay = (value: string | null): value is string => !!value && /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value));
const dayLabel = (day: string) => new Date(`${day}T12:00:00`).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
const hhmm = (minutes: number) => `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`;
const sameInstant = (a: string, b: string) => Date.parse(a) === Date.parse(b);
const nameOf = (employee: Employee | undefined) => employee ? `${employee.firstName} ${employee.lastName}` : '';
const failureText = (failure: unknown, fallback = 'Could not save the change. Reload and try again.') =>
  failure instanceof Error && failure.message ? failure.message : fallback;

function hoursFor(employee: Employee | undefined, day: string) {
  const hours = employee?.defaultHours?.[weekdayOf(day)];
  return hours?.start && hours?.end ? `${hours.start.slice(0, 5)}–${hours.end.slice(0, 5)}` : 'No hours set';
}

/** Side-by-side lanes for visits that overlap in one column, so none hides another. */
function lanes(items: Block[]) {
  const sorted = [...items].sort((a, b) => a.start - b.start || a.end - b.end);
  const placed = new Map<string, { lane: number; lanes: number }>();
  let cluster: Block[] = []; let clusterEnd = -1; let laneEnds: number[] = [];
  const flush = () => { cluster.forEach(item => { placed.get(item.key)!.lanes = laneEnds.length; }); cluster = []; laneEnds = []; clusterEnd = -1; };
  for (const item of sorted) {
    if (cluster.length && item.start >= clusterEnd) flush();
    let lane = laneEnds.findIndex(end => end <= item.start);
    if (lane === -1) { lane = laneEnds.length; laneEnds.push(item.end); } else laneEnds[lane] = item.end;
    placed.set(item.key, { lane, lanes: 0 }); cluster.push(item); clusterEnd = Math.max(clusterEnd, item.end);
  }
  flush();
  return placed;
}

/** A design dialog: focus moves in when it opens, Escape closes it, and focus returns to what opened it. */
function Dialog({ label, onClose, busy, children }: { label: string; onClose: () => void; busy: boolean; children: ReactNode }) {
  const box = useRef<HTMLElement>(null);
  // What had focus when the dialog first rendered: the button that opened it.
  const opener = useRef(typeof document === 'undefined' ? null : document.activeElement as HTMLElement | null);
  const close = useRef(onClose); close.current = onClose;
  const blocked = useRef(busy); blocked.current = busy;
  useEffect(() => {
    const back = opener.current;
    box.current?.querySelector<HTMLElement>('select, input:not([hidden]), textarea, button:not([aria-label^="Close"])')?.focus();
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape' && !blocked.current) { event.preventDefault(); close.current(); } };
    document.addEventListener('keydown', onKey);
    // Focus goes back only once the dialog is really gone (a development remount runs this cleanup too).
    const section = box.current;
    return () => { document.removeEventListener('keydown', onKey); setTimeout(() => { if (!section?.isConnected && back?.isConnected) back.focus(); }, 0); };
  }, []);
  return <div role="dialog" aria-modal="true" aria-label={label} className="dlg"><section ref={box}>{children}</section></div>;
}

export default function SchedulerPage() {
  const [anchor, setAnchor] = useState(() => isoDate(new Date()));
  const [view, setView] = useState<'day' | 'week'>('day');
  const [chosen, setChosen] = useState<string>('all');
  const [weekEmployee, setWeekEmployee] = useState<string | null>(null);
  const [schedule, setSchedule] = useState<EmployeeDaySchedule[] | null>(null);
  const [overlaps, setOverlaps] = useState<OverlapFinding[]>([]);
  const [employees, setEmployees] = useState<Employee[] | null>(null);
  const [calendars, setCalendars] = useState<Record<string, AdminShiftCalendar>>({});
  const [calendarAccess, setCalendarAccess] = useState<'unknown' | 'yes' | 'no'>('unknown');
  // The server's reason when it refuses the calendar, shown instead of the default next step.
  const [deniedText, setDeniedText] = useState('');
  const [calendarError, setCalendarError] = useState('');
  const [timeZone, setTimeZone] = useState<string | undefined>(undefined);
  const [zoneError, setZoneError] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [showFullDay, setShowFullDay] = useState(false);
  const [showWeekends, setShowWeekends] = useState(false);
  const [ready, setReady] = useState(false);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<{ text: string; error?: boolean; undo?: () => Promise<void> } | null>(null);
  const dragged = useRef<Drag | null>(null);
  const [dropHint, setDropHint] = useState<{ column: string; start: number; end: number } | null>(null);
  const canDrag = useMediaQuery('(hover: hover) and (pointer: fine)', true);
  const opener = useRef<HTMLButtonElement | null>(null);
  const closeButton = useRef<HTMLButtonElement>(null);

  // Dialogs
  const [newVisit, setNewVisit] = useState<{ employeeId: string; day: string } | null>(null);
  const [workOrders, setWorkOrders] = useState<Shift[] | null>(null);
  const [newBranch, setNewBranch] = useState('');
  const [newStart, setNewStart] = useState('08:00');
  const [newLength, setNewLength] = useState(60);
  const [newError, setNewError] = useState('');
  const [offerTo, setOfferTo] = useState('');
  const [workFor, setWorkFor] = useState<Block | null>(null);
  const [workStart, setWorkStart] = useState(''); const [workEnd, setWorkEnd] = useState(''); const [workReason, setWorkReason] = useState('');
  const [workError, setWorkError] = useState('');
  const [photoFor, setPhotoFor] = useState<Block | null>(null);
  const [photos, setPhotos] = useState<ShiftPhoto[]>([]);
  const [photoReason, setPhotoReason] = useState('');
  const [photoError, setPhotoError] = useState('');
  const fileInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    // A link with a date opens that day; a link with an employee opens that employee's week.
    const params = new URLSearchParams(window.location.search);
    const employee = params.get('employee');
    const day = params.get('date');
    if (isDay(day)) setAnchor(day);
    if (employee) { setWeekEmployee(employee); setView('week'); }
    setReady(true);
    adminTenant.get()
      .then(tenant => { if (tenant?.timeZoneId) setTimeZone(tenant.timeZoneId); else setZoneError(true); })
      .catch(() => setZoneError(true));
    // Work times and photos use the shift calendar, which an owner enables per admin.
    authorizedFetch('/api/v1/me').then(response => response.ok ? response.json() : null)
      .then(identity => setCalendarAccess(identity?.canUseShiftCalendar === true ? 'yes' : 'no')).catch(() => setCalendarAccess('no'));
  }, []);

  const weekStart = mondayOf(anchor);
  const weekEnd = addDays(weekStart, DAYS - 1);
  const shownDays = view === 'week' ? Array.from({ length: showWeekends ? DAYS : 5 }, (_, index) => addDays(weekStart, index)) : [anchor];
  const shownKey = shownDays.join(',');

  useEffect(() => {
    if (!ready) return;
    let cancelled = false;
    setLoading(true); setError('');
    Promise.all([adminShifts.employeeSchedule(weekStart, weekEnd), adminShifts.overlapAudit(weekStart, weekEnd), adminEmployees.list()])
      .then(([scheduleRows, overlapRows, staff]) => {
        if (cancelled) return;
        // A missing overlap result is a failed load, not zero overlaps: a double booking must never look clear.
        if (!scheduleRows || !staff || !overlapRows) { setError('Could not load the schedule. Check your connection, then retry.'); setSchedule(null); return; }
        setSchedule(scheduleRows); setOverlaps(overlapRows); setEmployees(staff);
      })
      .catch(() => { if (!cancelled) setError('Could not load the schedule. Check your connection, then retry.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [ready, weekStart, weekEnd, reloadKey]);

  useEffect(() => {
    // Work times, review flags and cancelled visits come from the calendar, one request per shown day.
    if (!ready || calendarAccess !== 'yes') return;
    let cancelled = false;
    Promise.all(shownKey.split(',').map(day => calendarRequest<AdminShiftCalendar>(`?date=${encodeURIComponent(day)}`).then(result => [day, result] as const)))
      .then(rows => { if (!cancelled) { setCalendars(Object.fromEntries(rows)); setCalendarError(''); } })
      .catch(failure => {
        if (cancelled) return;
        if (failure instanceof CalendarAccessError) { setCalendarAccess('no'); setDeniedText(failure.message); setCalendarError(''); }
        else setCalendarError(failureText(failure, 'The calendar could not be loaded. Reload the page to try again.'));
      });
    return () => { cancelled = true; };
  }, [ready, calendarAccess, shownKey, reloadKey]);

  // The page refreshes on its own: every minute and when the window regains focus.
  useEffect(() => {
    const reload = () => { if (!dragged.current) setReloadKey(key => key + 1); };
    const interval = window.setInterval(reload, 60_000);
    window.addEventListener('focus', reload);
    return () => { window.clearInterval(interval); window.removeEventListener('focus', reload); };
  }, []);

  const staff = useMemo(() => (employees ?? []).filter(employee => employee.isActive && employee.role !== 'partner'), [employees]);
  const weekPerson = staff.find(employee => employee.id === weekEmployee) ?? staff[0];

  const formatTime = (value: string) => new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone }).format(new Date(value));
  const local = (value: string) => {
    const parts = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone })
      .formatToParts(new Date(value));
    const get = (type: string) => parts.find(part => part.type === type)?.value ?? '00';
    return { day: `${get('year')}-${get('month')}-${get('day')}`, minutes: Number(get('hour')) * 60 + Number(get('minute')) };
  };
  /** A workplace-local day and minute as a UTC instant, correct across clock changes. */
  const zonedToUtc = (day: string, minutes: number) => {
    const [y, m, d] = day.split('-').map(Number);
    const wanted = Date.UTC(y, m - 1, d, Math.floor(minutes / 60), minutes % 60);
    let instant = wanted;
    for (let attempt = 0; attempt < 3; attempt++) {
      const seen = local(new Date(instant).toISOString());
      const [sy, sm, sd] = seen.day.split('-').map(Number);
      const shown = Date.UTC(sy, sm - 1, sd, Math.floor(seen.minutes / 60), seen.minutes % 60);
      if (shown === wanted) break;
      instant += wanted - shown;
    }
    return new Date(instant).toISOString();
  };
  const sameVisit = (a: ScheduleVisit, b: ScheduleVisit) => a.shiftId === b.shiftId && a.startUtc === b.startUtc;
  const span = (startUtc: string, endUtc: string) => {
    const start = local(startUtc); const end = local(endUtc);
    return { start: start.minutes, end: end.day === start.day ? Math.max(end.minutes, start.minutes + 15) : 24 * 60 };
  };

  const blocksFor = (employeeId: string, day: string): Block[] => {
    const calendar = calendars[day];
    const scheduled = (schedule?.find(row => row.employeeId === employeeId && row.localDay.slice(0, 10) === day)?.visits ?? []).map(visit => {
      const calVisit = calendar?.occurrences.find(item => item.shiftId === visit.shiftId && sameInstant(item.startUtc, visit.startUtc)
        && item.assignees.some(person => person.employeeId === employeeId)) ?? null;
      return {
        key: `${employeeId}|${visit.shiftId}|${visit.startUtc}`, employeeId, day, ...span(visit.startUtc, visit.endUtc), kind: kindOf(visit.workStatus),
        overlap: overlaps.some(finding => finding.employeeId === employeeId && (sameVisit(finding.earlier, visit) || sameVisit(finding.later, visit))),
        shiftId: visit.shiftId, name: visit.shiftName, startUtc: visit.startUtc, endUtc: visit.endUtc,
        place: [visit.branchName, visit.companyName].filter(Boolean).join(' · '),
        visit, calVisit, person: calVisit?.assignees.find(person => person.employeeId === employeeId) ?? null,
      } satisfies Block;
    });
    // Cancelled visits are not on the schedule; the calendar still lists them for their assignee.
    const cancelled = (calendar?.occurrences ?? []).filter(item => item.cancelled && item.assignees.some(person => person.employeeId === employeeId)).map(item => ({
      key: `${employeeId}|${item.shiftId}|${item.startUtc}|cancelled`, employeeId, day, ...span(item.startUtc, item.endUtc), kind: 'cancelled' as Kind, overlap: false,
      shiftId: item.shiftId, name: item.shiftName, startUtc: item.startUtc, endUtc: item.endUtc,
      place: [item.branchName, item.companyName].filter(Boolean).join(' · '),
      visit: null, calVisit: item, person: item.assignees.find(person => person.employeeId === employeeId) ?? null,
    } satisfies Block));
    return [...scheduled, ...cancelled];
  };

  const columns: Column[] = !timeZone ? [] : view === 'week'
    ? (weekPerson ? shownDays.map(day => {
      const blocks = blocksFor(weekPerson.id, day);
      return { key: day, name: new Date(`${day}T12:00:00`).toLocaleDateString('en-GB', { weekday: 'short' }),
        sub: new Date(`${day}T12:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' }),
        hours: `${hoursFor(weekPerson, day)} · ${plural(blocks.length, 'visit', 'visits')}`, employeeId: weekPerson.id, day, blocks };
    }) : [])
    : staff.filter(employee => chosen === 'all' || employee.id === chosen).map(employee => {
      const blocks = blocksFor(employee.id, anchor);
      return { key: employee.id, name: nameOf(employee), sub: dayLabel(anchor), hours: `${hoursFor(employee, anchor)} · ${plural(blocks.length, 'visit', 'visits')}`,
        employeeId: employee.id, day: anchor, blocks };
    });
  const allBlocks = columns.flatMap(column => column.blocks);
  // Weekend visits of the week's employee while Monday to Friday is shown, so they are never missed.
  const weekendVisits = view === 'week' && !showWeekends && weekPerson ? [5, 6].reduce((count, index) => count + blocksFor(weekPerson.id, addDays(weekStart, index)).length, 0) : 0;
  const selected = allBlocks.find(item => item.key === selectedKey) ?? null;

  // 07:00–19:00 by default, widened to fit any earlier or later visit; Show 24 h shows the whole day.
  const firstHour = showFullDay ? 0 : Math.max(0, Math.min(7, ...allBlocks.map(item => Math.floor(item.start / 60))));
  const lastHour = showFullDay ? 24 : Math.min(24, Math.max(19, ...allBlocks.map(item => Math.ceil(item.end / 60))));
  const colHeight = (lastHour - firstHour) * HOUR_PX;

  const detailsOpen = selected !== null;
  const closeDetails = () => {
    setSelectedKey(null);
    const back = opener.current;
    setTimeout(() => { if (back?.isConnected) back.focus(); }, 0);
  };
  useEffect(() => { if (detailsOpen) closeButton.current?.focus(); }, [detailsOpen]);
  const dialogOpen = !!newVisit || !!workFor || !!photoFor;
  useEffect(() => {
    if (!detailsOpen || dialogOpen) return;
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape' && !event.defaultPrevented) closeDetails(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [detailsOpen, dialogOpen]);

  const rangeLabel = view === 'week'
    ? `Week of ${new Date(`${weekStart}T12:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'long' })}${weekPerson ? ` for ${nameOf(weekPerson)}` : ''}`
    : `Day view, ${dayLabel(anchor)}`;
  const step = (count: number) => { setSelectedKey(null); setAnchor(view === 'week' ? addDays(weekStart, count * DAYS) : addDays(anchor, count)); };
  const today = () => { setSelectedKey(null); setAnchor(isoDate(new Date())); };
  const showWeek = () => { setSelectedKey(null); setWeekEmployee(weekEmployee ?? (chosen !== 'all' ? chosen : staff[0]?.id ?? null)); setView('week'); };
  const showDay = () => { setSelectedKey(null); setView('day'); };

  if (zoneError) return (
    <div className="scheduler">
      <h1>Scheduler</h1>
      <div role="alert" className="msg error"><span>Could not load the workplace time zone, so visit times cannot be shown correctly. Reload the page to try again.</span>
        <button type="button" className="btn btn-ghost" onClick={() => window.location.reload()}>Reload</button></div>
    </div>
  );
  if (!timeZone) return <div className="scheduler"><h1>Scheduler</h1><Loader aria-label="Loading schedule" /></div>;

  // ---- Moving, resizing and reassigning ----
  const isManual = (visit: ScheduleVisit) => visit.status === STATUS_EXTRA;
  const locked = (block: Block) => KIND[block.kind].locked || !block.visit || !!block.visit.moveLockReason || typeof block.visit.rosterVersion !== 'string';
  const isOffer = (block: Block) => block.kind === 'offered' && !!block.visit?.offerPending && !!block.visit.extraExceptionId;
  const movable = (block: Block) => canDrag && !saving && (!locked(block) || isOffer(block));
  const canDropOn = (drag: Drag, column: Column) => isOffer(drag.block) ? !drag.resize && column.employeeId !== drag.block.employeeId && column.day === drag.block.day
    : column.employeeId === drag.block.employeeId && (!drag.resize || column.day === drag.block.day)
    || (!drag.resize && !!drag.block.visit && !drag.block.visit.isBranchMain && !drag.block.visit.reassignLockReason
      && !(drag.block.visit.assigneeIds ?? []).includes(column.employeeId));

  const move = (visit: ScheduleVisit, startUtc: string, endUtc: string, reason: string, exceptionId?: string | null) =>
    adminShifts.saveOccurrenceException(visit.shiftId, {
      id: (isManual(visit) ? visit.extraExceptionId ?? visit.exceptionId : exceptionId ?? visit.exceptionId) ?? null,
      originalStartUtc: isManual(visit) ? null : visit.originalStartUtc ?? visit.startUtc,
      startUtc, endUtc, status: isManual(visit) ? STATUS_EXTRA : STATUS_MOVED, reason,
      // A move replaces the employee on the visit's record, so the current one is sent back unchanged.
      employeeId: visit.visitEmployeeId ?? null,
    });
  const reassign = (visit: ScheduleVisit, from: string, to: string, reason: string) =>
    adminShifts.setVisitAssignees(visit.shiftId, {
      originalStartUtc: isManual(visit) ? null : visit.originalStartUtc ?? visit.startUtc,
      extraExceptionId: isManual(visit) ? visit.extraExceptionId ?? visit.exceptionId ?? null : null,
      mode: MODE_REPLACE, employeeIds: [...(visit.assigneeIds ?? []).filter(id => id !== from), to],
      reason, rosterVersion: visit.rosterVersion ?? '', assignmentVersion: visit.assignmentVersion ?? null, fromThisDate: false, preview: false,
    });
  /** The visit as the server has it now, for an undo that needs fresh roster versions. */
  const freshVisit = async (employeeId: string, visit: ScheduleVisit, startUtc: string) => {
    const rows = await adminShifts.employeeSchedule(weekStart, weekEnd);
    const original = visit.originalStartUtc ?? (isManual(visit) ? null : visit.startUtc);
    return rows?.filter(row => row.employeeId === employeeId).flatMap(row => row.visits).find(item => item.shiftId === visit.shiftId
      && (isManual(visit) ? (item.extraExceptionId ?? item.exceptionId) === (visit.extraExceptionId ?? visit.exceptionId)
        : (item.originalStartUtc ?? item.startUtc) === original || Date.parse(item.startUtc) === Date.parse(startUtc)));
  };
  const when = (day: string, startUtc: string) => `${dayLabel(day)} ${formatTime(startUtc)}`;

  const pointerMinutes = (event: DragEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    return firstHour * 60 + ((event.clientY - rect.top) / HOUR_PX) * 60;
  };
  const snap = (minutes: number) => Math.round(minutes / SNAP_MINUTES) * SNAP_MINUTES;
  const target = (drag: Drag, event: DragEvent<HTMLDivElement>) => {
    const duration = drag.block.end - drag.block.start;
    if (drag.resize) {
      const end = Math.min(24 * 60, Math.max(drag.block.start + SNAP_MINUTES, snap(pointerMinutes(event))));
      return { start: drag.block.start, end };
    }
    const start = Math.min(24 * 60 - duration, Math.max(0, snap(pointerMinutes(event) - drag.grab)));
    return { start, end: start + duration };
  };
  const onColumnDragOver = (column: Column, event: DragEvent<HTMLDivElement>) => {
    const drag = dragged.current;
    if (!drag || !canDropOn(drag, column)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    const next = target(drag, event);
    if (dropHint?.column !== column.key || dropHint.start !== next.start || dropHint.end !== next.end) setDropHint({ column: column.key, ...next });
  };
  const onColumnDrop = async (column: Column, event: DragEvent<HTMLDivElement>) => {
    const drag = dragged.current;
    dragged.current = null; setDropHint(null);
    if (!drag || !drag.block.visit || !canDropOn(drag, column)) return;
    event.preventDefault();
    const { block } = drag; const visit = drag.block.visit;
    if (isOffer(block)) { await reoffer(block, column.employeeId); return; }
    const next = target(drag, event);
    const startUtc = zonedToUtc(column.day, next.start);
    const endUtc = drag.resize ? zonedToUtc(column.day, next.end) : new Date(Date.parse(startUtc) + (Date.parse(visit.endUtc) - Date.parse(visit.startUtc))).toISOString();
    const timeChanged = !sameInstant(startUtc, visit.startUtc) || !sameInstant(endUtc, visit.endUtc);
    const employeeChanged = column.employeeId !== block.employeeId;
    if (!timeChanged && !employeeChanged) return;
    const from = staff.find(employee => employee.id === block.employeeId);
    const to = staff.find(employee => employee.id === column.employeeId);
    setSaving(true); setNotice({ text: 'Saving…' }); setSelectedKey(null);
    let reassigned = false;
    try {
      // Reassign first: it checks the roster version, so a stale schedule is refused before anything changes.
      if (employeeChanged) { await reassign(visit, block.employeeId, column.employeeId, ASSIGN_REASON); reassigned = true; }
      const moved = timeChanged ? await move(visit, startUtc, endUtc, drag.resize ? RESIZE_REASON : MOVE_REASON) : null;
      const text = drag.resize ? `Changed ${visit.shiftName} to end at ${formatTime(endUtc)}.`
        : `${[employeeChanged ? `Reassigned ${visit.shiftName} from ${nameOf(from)} to ${nameOf(to)}` : `Moved ${visit.shiftName}`,
          timeChanged ? `${employeeChanged ? 'and moved it' : ''} to ${when(column.day, startUtc)}`.trim() : ''].filter(Boolean).join(' ')}.`;
      setNotice({ text, undo: async () => {
        if (moved) await move(visit, visit.startUtc, visit.endUtc, UNDO_REASON, moved.exceptionId);
        if (employeeChanged) {
          const current = await freshVisit(column.employeeId, visit, startUtc);
          if (!current) throw new Error('The visit changed again, so it cannot be undone here. Reload and check it.');
          await reassign(current, column.employeeId, block.employeeId, UNDO_REASON);
        }
      } });
    } catch (failure) {
      setNotice({ error: true, text: reassigned
        ? `Reassigned ${visit.shiftName} to ${nameOf(to)}, but the time could not be changed: ${failureText(failure)}`
        : failureText(failure),
        undo: reassigned ? async () => {
          const current = await freshVisit(column.employeeId, visit, visit.startUtc);
          if (!current) throw new Error('The visit changed again, so it cannot be undone here. Reload and check it.');
          await reassign(current, column.employeeId, block.employeeId, UNDO_REASON);
        } : undefined });
    } finally {
      setSaving(false); setReloadKey(key => key + 1);
    }
  };
  /** Offers a visit waiting for acceptance to another employee instead; Undo offers it back. */
  const reoffer = async (block: Block, to: string) => {
    const visit = block.visit!;
    const fromName = nameOf(staff.find(employee => employee.id === block.employeeId));
    const toName = nameOf(staff.find(employee => employee.id === to));
    setSaving(true); setNotice({ text: 'Saving…' }); setSelectedKey(null);
    try {
      const result = await adminBranchVisits.setEmployee(visit.shiftId, visit.extraExceptionId!, to, visit.offerRevision ?? 0);
      if (!result.ok) { setNotice({ error: true, text: result.message }); return; }
      const accepted = result.value.status === 'Assigned';
      setNotice({ text: accepted
        ? `${toName} accepts work automatically, so ${visit.shiftName} is now assigned to them instead of waiting for ${fromName}.`
        : `Offered ${visit.shiftName} to ${toName} instead of ${fromName}. It waits for ${toName} to accept.`,
        undo: async () => {
          const back = await adminBranchVisits.setEmployee(visit.shiftId, visit.extraExceptionId!, block.employeeId, result.value.offerRevision);
          if (!back.ok) throw new Error(back.message);
        } });
    } catch (failure) {
      setNotice({ error: true, text: failureText(failure) });
    } finally {
      setSaving(false); setReloadKey(key => key + 1);
    }
  };
  const undoLast = async () => {
    const undo = notice?.undo;
    if (!undo) return;
    setSaving(true); setNotice({ text: 'Undoing…' });
    try { await undo(); setNotice({ text: 'Change undone.' }); }
    catch (failure) { setNotice({ error: true, text: failureText(failure) }); }
    finally { setSaving(false); setReloadKey(key => key + 1); }
  };

  // ---- New visit ----
  const openNewVisit = (column: Column) => {
    setNewVisit({ employeeId: column.employeeId, day: column.day }); setNewBranch(''); setNewStart('08:00'); setNewLength(60); setNewError('');
    if (!workOrders) adminShifts.list().then(rows => setWorkOrders((rows ?? []).filter(shift => shift.isActive && shift.isBranchMain)));
  };
  const newEnd = (() => { const [h, m] = newStart.split(':').map(Number); return Math.min(24 * 60 - 1, h * 60 + m + newLength); })();
  const saveNewVisit = async () => {
    if (!newVisit || saving) return;
    if (!newBranch) { setNewError('Choose a work order.'); return; }
    setSaving(true); setNewError('');
    const result = await adminBranchVisits.add({ branchId: newBranch, fromDate: newVisit.day, toDate: newVisit.day, startTime: newStart, endTime: hhmm(newEnd),
      notes: null, employeeId: newVisit.employeeId, useDefaultEmployee: false, cleaningType: 'Normal' });
    setSaving(false);
    if (!result.ok) { setNewError(result.message); return; }
    const order = workOrders?.find(shift => shift.branchId === newBranch);
    setNotice({ text: `Scheduled ${order?.name ?? 'the visit'} for ${nameOf(staff.find(e => e.id === newVisit.employeeId))} on ${dayLabel(newVisit.day)}, ${newStart}–${hhmm(newEnd)}.` });
    setNewVisit(null); setReloadKey(key => key + 1);
  };

  // ---- Work times ----
  const tz = timeZone;
  const openWork = (block: Block) => {
    if (!block.person) return;
    setWorkFor(block); setWorkReason(''); setWorkError('');
    setWorkStart(calendarLocalInput(block.person.actualStartUtc ?? block.startUtc, tz));
    setWorkEnd(calendarLocalInput(block.person.actualEndUtc ?? block.endUtc, tz));
  };
  const timeOptions = (day: string, current: string) => {
    const slots = Array.from({ length: 96 }, (_, index) => `${day}T${hhmm(index * 15)}`);
    return (current && !slots.includes(current) ? [...slots, current].sort() : slots);
  };
  const workDuration = workFor ? calendarDuration(workStart, workEnd, tz) : null;
  const saveWork = async () => {
    if (!workFor?.person || saving) return;
    if (!workStart || !workEnd || !workReason.trim()) { setWorkError('Enter both times and a reason.'); return; }
    if (workEnd <= workStart) { setWorkError('End must be after start.'); return; }
    setSaving(true); setWorkError('');
    try {
      await calendarRequest('/work-time', { method: 'PUT', body: JSON.stringify({ shiftId: workFor.shiftId, employeeId: workFor.employeeId,
        occurrenceStartUtc: workFor.calVisit?.startUtc ?? workFor.startUtc, exceptionId: workFor.calVisit?.exceptionId ?? null,
        actualStartLocal: workStart, actualEndLocal: workEnd, reason: workReason.trim(), version: workFor.person.version }) });
      setWorkFor(null); setNotice({ text: 'Work times saved.' }); setReloadKey(key => key + 1);
    } catch (failure) {
      setWorkError(failureText(failure, 'Could not save times.'));
      if (failure instanceof CalendarAccessError) { setCalendarAccess('no'); setDeniedText(failure.message); }
    } finally { setSaving(false); }
  };

  // ---- Photos ----
  const openPhotos = (block: Block) => { setPhotoFor(block); setPhotos([]); setPhotoReason(''); setPhotoError(''); };
  const addPhotos = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    try {
      const urls = await readShiftPhotos(Array.from(files), photos);
      setPhotos(current => [...current, ...urls.map(url => ({ url, note: '' }))]); setPhotoError('');
    } catch (failure) { setPhotoError(failureText(failure, 'Could not read the photo.')); }
  };
  const savePhotos = async () => {
    if (!photoFor?.person || saving) return;
    if (photos.length === 0) { setPhotoError('Choose at least one photo.'); return; }
    if (photos.some(photo => !photo.note.trim())) { setPhotoError(photoNoteMessage); return; }
    if (!photoReason.trim()) { setPhotoError('Enter a reason.'); return; }
    setSaving(true); setPhotoError('');
    try {
      await calendarRequest('/work-photos', { method: 'POST', body: JSON.stringify({ shiftId: photoFor.shiftId, employeeId: photoFor.employeeId,
        occurrenceStartUtc: photoFor.calVisit?.startUtc ?? photoFor.startUtc, exceptionId: photoFor.calVisit?.exceptionId ?? null,
        reason: photoReason.trim(), photos: photos.map(photo => ({ url: photo.url, note: photo.note.trim() })), version: photoFor.person.version }) });
      setPhotoFor(null); setNotice({ text: 'Photos added to the visit.' }); setReloadKey(key => key + 1);
    } catch (failure) {
      setPhotoError(failureText(failure, 'Could not add the photos.'));
      if (failure instanceof CalendarAccessError) { setCalendarAccess('no'); setDeniedText(failure.message); }
    } finally { setSaving(false); }
  };

  // ---- Rendering ----
  const blockButton = (block: Block, style: CSSProperties) => {
    const kind = KIND[block.kind];
    const time = `${formatTime(block.startUtc)}–${formatTime(block.endUtc)}`;
    const review = block.person?.review;
    const canMove = movable(block);
    return <button key={block.key} type="button" style={style}
      className={`blk ${kind.block}${block.overlap ? ' blk-over' : ''}${canMove ? '' : ' locked'}`}
      aria-pressed={selectedKey === block.key}
      aria-label={`${block.name}, ${time}, ${kind.text}${block.overlap ? ', overlaps another visit' : ''}${review ? ', needs review' : ''}`}
      title={!canMove && block.visit?.moveLockReason ? block.visit.moveLockReason : undefined}
      onClick={event => { opener.current = event.currentTarget; setSelectedKey(block.key); }}
      {...(canMove ? {
        draggable: true,
        onDragStart: (event: DragEvent<HTMLButtonElement>) => {
          const rect = event.currentTarget.getBoundingClientRect();
          dragged.current = { block, grab: Math.round(((event.clientY - rect.top) / HOUR_PX) * 60), resize: false };
          event.dataTransfer.effectAllowed = 'move'; event.dataTransfer.setData('text/plain', block.key);
        },
        onDragEnd: () => { dragged.current = null; setDropHint(null); },
      } : {})}>
      <strong>{block.name}</strong>
      <span>{time}</span>
      <span>{kind.text}</span>
      {review && <em className="tag">Needs review</em>}
    </button>;
  };

  const details = selected && (() => {
    const kind = KIND[selected.kind];
    const visit = selected.visit;
    const person = selected.person;
    const minutes = Math.round((Date.parse(selected.endUtc) - Date.parse(selected.startUtc)) / 60000);
    const duration = minutes % 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60} min` : `${minutes / 60} h`;
    const assignee = staff.find(employee => employee.id === selected.employeeId);
    const actual = person?.actualStartUtc
      ? `${formatTime(person.actualStartUtc)} – ${person.actualEndUtc ? formatTime(person.actualEndUtc) : 'Not ended'}${person.hours == null ? '' : ` · ${person.hours.toFixed(2)} hours`}`
      : 'Not recorded';
    const checklist = visit?.checklistTotal ? `${visit.checklistDone ?? 0} of ${visit.checklistTotal} done` : 'No checklist yet';
    const reviewKind = person?.review ? (person.review.kind === 'missing-completion' ? 'Missing completion' : 'Unusual time') : '';
    const access = calendarAccess === 'yes';
    const canRecord = access && !!person?.canRecord && !selected.calVisit?.ambiguous;
    const canPhoto = access && !!person?.version;
    const actionNote = selected.kind === 'offered' ? 'Work times and photos can be added once the visit is accepted.'
      : !access ? deniedText || 'Shift calendar access is needed to record work times and add photos. Ask an owner to enable it, then reload.'
      : selected.kind === 'cancelled' ? 'Cancelled visits have no work times or photos.'
      : selected.calVisit?.ambiguous ? 'Overlapping visits need review before work times can be recorded.'
      : !canRecord ? 'Start and end times and photos can be added once the visit day has passed, if the assignee did not record them.'
      : !canPhoto ? 'Photos can be added once start and end times are recorded.' : '';
    const lockText = selected.kind === 'offered'
      ? `Waiting for ${nameOf(assignee)} to accept.${canDrag ? ' Drag it to another employee to offer it to them instead.' : ''}`
      : kind.locked ? `Status: ${kind.text}. It cannot be moved or reassigned now.` : visit?.moveLockReason ?? '';
    return <aside className="panel" aria-label="Visit details">
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12 }}>
        <div style={{ minWidth: 0 }}>
          <h2 style={{ margin: 0, fontSize: 18 }}>{selected.name}</h2>
          {selected.place && <p className="note" style={{ marginTop: 4 }}>{selected.place}</p>}
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginTop: 10 }}>
            <span className={kind.chip}>{kind.text}</span>
            {selected.overlap && <span className="chip chip-needs">Overlap</span>}
            {reviewKind && <span className="chip chip-needs">{reviewKind}</span>}
          </div>
        </div>
        <button ref={closeButton} type="button" className="btn btn-ghost x" aria-label="Close visit details" onClick={closeDetails}>×</button>
      </div>
      <dl style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '10px 16px', fontSize: 14, margin: '16px 0' }}>
        <dt style={{ color: 'var(--muted)' }}>Visit</dt><dd style={{ margin: 0, fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>{dayLabel(selected.day)}, {formatTime(selected.startUtc)}–{formatTime(selected.endUtc)}</dd>
        {visit?.visitNumber != null && <><dt style={{ color: 'var(--muted)' }}>Number</dt><dd style={{ margin: 0 }}>Visit {visit.visitNumber}</dd></>}
        <dt style={{ color: 'var(--muted)' }}>{selected.kind === 'offered' ? 'Offered to' : 'Assignee'}</dt><dd style={{ margin: 0, fontWeight: 600 }}>{nameOf(assignee) || person?.name}</dd>
        <dt style={{ color: 'var(--muted)' }}>Duration</dt><dd style={{ margin: 0 }}>{duration}</dd>
        <dt style={{ color: 'var(--muted)' }}>Actual</dt><dd style={{ margin: 0, fontVariantNumeric: 'tabular-nums' }}>{actual}</dd>
        <dt style={{ color: 'var(--muted)' }}>Checklist</dt><dd style={{ margin: 0 }}>{checklist}</dd>
        <dt style={{ color: 'var(--muted)' }}>Notes</dt><dd style={{ margin: 0 }}>{visit?.visitNotes || 'No notes'}</dd>
        {person?.review && <><dt style={{ color: 'var(--muted)' }}>Review</dt><dd style={{ margin: 0 }}>{person.review.label}</dd></>}
      </dl>
      {selected.overlap && <p style={{ margin: '0 0 12px', padding: 12, border: '1px solid var(--gold-ink)', borderRadius: 12, background: 'var(--gold-wash)', fontSize: 14 }}>This visit overlaps another visit by the same assignee. Move or reassign one of them.</p>}
      {lockText && <p className="note" style={{ marginBottom: 12 }}>{lockText}</p>}
      {isOffer(selected) && <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginBottom: 16 }}>
        <label className="field">Offer to someone else<select className="input" value={offerTo} onChange={event => setOfferTo(event.currentTarget.value)}>
          <option value="">Choose an employee</option>
          {staff.filter(employee => employee.id !== selected.employeeId).map(employee => <option key={employee.id} value={employee.id}>{nameOf(employee)}</option>)}
        </select></label>
        <button type="button" className="btn btn-primary" disabled={!offerTo || saving} onClick={() => { void reoffer(selected, offerTo); setOfferTo(''); }}>Send offer</button>
      </div>}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <button type="button" className="btn btn-primary" disabled={!canRecord} onClick={() => openWork(selected)}>{person?.actualStartUtc ? 'Edit work times' : 'Record start and end'}</button>
        <button type="button" className="btn btn-ghost" disabled={!canPhoto} onClick={() => openPhotos(selected)}>Add photos</button>
        {actionNote && <p className="note">{actionNote}</p>}
      </div>
    </aside>;
  })();

  const workTitle = workFor?.person?.actualStartUtc ? 'Edit work times' : 'Record start and end';
  const noVisits = !loading && !error && schedule && staff.length > 0 && columns.length > 0 && allBlocks.length === 0;

  return (
    <div className="scheduler">
      <header>
        <h1>Scheduler</h1>
        <p className="note" style={{ marginTop: 4, fontSize: 15 }}>{rangeLabel}. Assigned visits only; unassigned work is in the <a href="/admin/work-queue/">Work queue</a>. Drag a visit to change its time{view === 'week' ? ' or day' : ' or assignee'}. Click a visit for its details and work times. Work times can be recorded for past dates only.</p>
      </header>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
        <button type="button" className="btn btn-ghost" onClick={() => step(-1)} aria-label="Previous">Previous</button>
        <button type="button" className="btn btn-ghost" onClick={today}>Today</button>
        <button type="button" className="btn btn-ghost" onClick={() => step(1)} aria-label="Next">Next</button>
        <div style={{ marginLeft: 'auto', display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
          {staff.length > 0 && (view === 'day'
            ? <label className="field">Employees<select className="input" aria-label="Employees" value={chosen} onChange={event => { setSelectedKey(null); setChosen(event.currentTarget.value); }}>
              <option value="all">All employees</option>
              {staff.map(employee => <option key={employee.id} value={employee.id}>{nameOf(employee)}</option>)}
            </select></label>
            : <label className="field">Employee<select className="input" aria-label="Employee" value={weekPerson?.id ?? ''} onChange={event => { setSelectedKey(null); setWeekEmployee(event.currentTarget.value); }}>
              {staff.map(employee => <option key={employee.id} value={employee.id}>{nameOf(employee)}</option>)}
            </select></label>)}
          <div className="seg" role="group" aria-label="View">
            <button type="button" aria-pressed={view === 'day'} onClick={showDay}>Day</button>
            <button type="button" aria-pressed={view === 'week'} onClick={showWeek}>Week</button>
          </div>
        </div>
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', alignItems: 'center', gap: 20 }}>
        <label className="check"><input type="checkbox" checked={showFullDay} onChange={event => setShowFullDay(event.currentTarget.checked)} />Show 24 h</label>
        {view === 'week' && <label className="check"><input type="checkbox" checked={showWeekends} onChange={event => setShowWeekends(event.currentTarget.checked)} />Show weekends</label>}
      </div>

      {notice && <div role="status" className={`msg${notice.error ? ' error' : ''}`}>
        <span>{notice.text}</span>
        <span style={{ display: 'flex', gap: 8 }}>
          {notice.undo && !saving && <button type="button" className="btn btn-ghost" onClick={() => { void undoLast(); }}>Undo</button>}
          {!saving && <button type="button" className="btn btn-ghost x" aria-label="Dismiss message" onClick={() => setNotice(null)}>×</button>}
        </span>
      </div>}
      {loading && !schedule && <Loader aria-label="Loading schedule" />}
      {calendarError && !error && <div role="alert" className="msg error"><span>{calendarError}</span><button type="button" className="btn btn-ghost" onClick={() => setReloadKey(key => key + 1)}>Retry</button></div>}
      {error && <div role="alert" className="msg error"><span>{error}</span><button type="button" className="btn btn-ghost" onClick={() => setReloadKey(key => key + 1)}>Retry</button></div>}
      {!error && schedule && staff.length === 0 && <p style={{ margin: 0 }}>No active employees yet. Add employees, then their visits appear here.</p>}
      {!error && schedule && weekendVisits > 0 && <p className="msg" style={{ margin: 0 }}>{plural(weekendVisits, 'weekend visit', 'weekend visits')} — tick Show weekends to see {weekendVisits === 1 ? 'it' : 'them'}.</p>}
      {noVisits && <p style={{ margin: 0 }}>{view === 'week' ? 'No visits this week.' : 'No visits on this day.'}</p>}

      {!error && schedule && columns.length > 0 && <>
        <div className="board" role="region" aria-label="Schedule grid" tabIndex={0}>
          <div className="grid" style={{ gridTemplateColumns: `56px repeat(${columns.length}, minmax(150px, 1fr))` }}>
            <div className="colhead" style={{ fontSize: 13, color: 'var(--muted)' }}>Time</div>
            {columns.map(column => <div key={column.key} className="colhead" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
              <span>{column.name}<small>{column.sub}</small><small>{column.hours}</small></span>
              <button type="button" className="btn btn-ghost" style={{ minHeight: 40, padding: '0 10px', fontSize: 13 }} aria-label={`Add visit for ${view === 'week' ? `${nameOf(weekPerson)} on ${dayLabel(column.day)}` : column.name}`} onClick={() => openNewVisit(column)}>+ Visit</button>
            </div>)}
            <div className="gutter" style={{ height: colHeight }} aria-hidden="true">
              {Array.from({ length: lastHour - firstHour + 1 }, (_, index) => <span key={index} style={{ top: Math.max(0, Math.min(colHeight - 18, index * HOUR_PX - 4)) }}>{hhmm((firstHour + index) * 60)}</span>)}
            </div>
            {columns.map(column => {
              const layout = lanes(column.blocks);
              return <div key={column.key} role="group" aria-label={`${view === 'week' ? nameOf(weekPerson) : column.name}, ${column.day}`}
                className={`col${dropHint?.column === column.key ? ' over' : ''}`} style={{ height: colHeight }}
                onDragOver={event => onColumnDragOver(column, event)}
                onDragLeave={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null) && dropHint?.column === column.key) setDropHint(null); }}
                onDrop={event => { void onColumnDrop(column, event); }}>
                {dropHint?.column === column.key && <div className="drop-marker" data-testid="drop-marker" aria-hidden="true"
                  style={{ top: ((dropHint.start - firstHour * 60) / 60) * HOUR_PX, height: Math.max(24, ((dropHint.end - dropHint.start) / 60) * HOUR_PX) }}>
                  <span className="chip chip-needs" style={{ margin: 4, fontSize: 11 }}>{hhmm(dropHint.start)}–{hhmm(dropHint.end)}</span>
                </div>}
                {column.blocks.map(block => {
                  const place = layout.get(block.key) ?? { lane: 0, lanes: 1 };
                  const width = 100 / Math.max(1, place.lanes);
                  const top = ((block.start - firstHour * 60) / 60) * HOUR_PX + 2;
                  const height = Math.max(28, ((block.end - block.start) / 60) * HOUR_PX - 4);
                  const left = `calc(${place.lane * width}% + 4px)`; const blockWidth = `calc(${width}% - 8px)`;
                  return <div key={block.key} style={{ display: 'contents' }}>
                    {blockButton(block, { top, height, left, width: blockWidth })}
                    {movable(block) && !isOffer(block) && <div className="handle" role="separator" aria-label={`Drag to change the end time of ${block.name}`} draggable
                      style={{ top: top + height - 8, left, width: blockWidth }}
                      onDragStart={event => { dragged.current = { block, grab: 0, resize: true }; event.dataTransfer.effectAllowed = 'move'; event.dataTransfer.setData('text/plain', block.key); }}
                      onDragEnd={() => { dragged.current = null; setDropHint(null); }} />}
                  </div>;
                })}
              </div>;
            })}
          </div>
        </div>
        <p className="note">Visits in progress, done or cancelled cannot be moved. Gold outline: overlaps another visit for the same assignee. Grey dashed: offered, waiting for the employee to accept.</p>
      </>}

      {details}

      {newVisit && <Dialog label="New visit" busy={saving} onClose={() => setNewVisit(null)}>
          <div className="dlg-head"><strong>New visit</strong><button type="button" className="btn btn-ghost x" aria-label="Close" disabled={saving} onClick={() => setNewVisit(null)}>×</button></div>
          <p className="note">{nameOf(staff.find(e => e.id === newVisit.employeeId))}, {dayLabel(newVisit.day)}</p>
          <label className="field">Work order<select className="input" value={newBranch} onChange={event => { setNewBranch(event.currentTarget.value); setNewError(''); }}>
            <option value="">{workOrders ? 'Choose a work order' : 'Loading work orders…'}</option>
            {(workOrders ?? []).map(shift => <option key={shift.id} value={shift.branchId}>{shift.name}</option>)}
          </select></label>
          <div className="two">
            <label className="field">Start<select className="input" value={newStart} onChange={event => setNewStart(event.currentTarget.value)}>
              {Array.from({ length: 96 }, (_, index) => hhmm(index * 15)).map(time => <option key={time} value={time}>{time}</option>)}
            </select></label>
            <label className="field">Length<select className="input" value={newLength} onChange={event => setNewLength(Number(event.currentTarget.value))}>
              {LENGTHS.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select></label>
          </div>
          <p className="note">Ends at {hhmm(newEnd)}.</p>
          {newError && <p role="alert" className="alert">{newError}</p>}
          <div className="dlg-foot">
            <button type="button" className="btn btn-ghost" disabled={saving} onClick={() => setNewVisit(null)}>Cancel</button>
            <button type="button" className="btn btn-primary" disabled={saving} onClick={() => { void saveNewVisit(); }}>Save visit</button>
          </div>
      </Dialog>}

      {workFor && <Dialog label={workTitle} busy={saving} onClose={() => setWorkFor(null)}>
          <div className="dlg-head"><strong>{workTitle}</strong><button type="button" className="btn btn-ghost x" aria-label="Close work times" disabled={saving} onClick={() => setWorkFor(null)}>×</button></div>
          <strong>{workFor.person?.name ?? nameOf(staff.find(e => e.id === workFor.employeeId))} · {workFor.name}</strong>
          <span style={{ fontSize: 14, fontVariantNumeric: 'tabular-nums' }}>Scheduled: {dayLabel(workFor.day)}, {formatTime(workFor.startUtc)} – {formatTime(workFor.endUtc)}</span>
          <p className="note">Confirm actual times in {tz}. Scheduled times are prefilled for new records. Saving records this past visit as completed. Checklist evidence is kept.</p>
          <div className="two">
            <label className="field">Actual start<select className="input" value={workStart} onChange={event => setWorkStart(event.currentTarget.value)}>
              {timeOptions(workFor.day, workStart).map(value => <option key={value} value={value}>{value.slice(0, 10) === workFor.day ? value.slice(11) : `${dayLabel(value.slice(0, 10))} ${value.slice(11)}`}</option>)}
            </select></label>
            <label className="field">Actual end<select className="input" value={workEnd} onChange={event => setWorkEnd(event.currentTarget.value)}>
              {timeOptions(workFor.day, workEnd).map(value => <option key={value} value={value}>{value.slice(0, 10) === workFor.day ? value.slice(11) : `${dayLabel(value.slice(0, 10))} ${value.slice(11)}`}</option>)}
            </select></label>
          </div>
          {workDuration !== null && <span style={{ fontSize: 14 }}>Work duration: {workDuration.toFixed(2)} hours</span>}
          <label className="field">Reason<input className="input" type="text" maxLength={2000} value={workReason} onChange={event => setWorkReason(event.currentTarget.value)} /></label>
          {workError && <p role="alert" className="alert">{workError}</p>}
          <div className="dlg-foot"><button type="button" className="btn btn-ghost" disabled={saving} onClick={() => setWorkFor(null)}>Cancel</button><button type="button" className="btn btn-primary" disabled={saving} onClick={() => { void saveWork(); }}>Save work times</button></div>
      </Dialog>}

      {photoFor && <Dialog label="Add photos" busy={saving} onClose={() => setPhotoFor(null)}>
          <div className="dlg-head"><strong>Add photos</strong><button type="button" className="btn btn-ghost x" aria-label="Close photos" disabled={saving} onClick={() => setPhotoFor(null)}>×</button></div>
          <strong>{photoFor.person?.name ?? nameOf(staff.find(e => e.id === photoFor.employeeId))} · {photoFor.name}</strong>
          <p className="note">For photos the employee sent you outside the app. Each photo needs its own note, and the reason is kept with the visit.</p>
          <input ref={fileInput} type="file" aria-label="Choose photos" accept="image/png,image/jpeg,image/webp,image/bmp" multiple hidden
            onChange={event => { void addPhotos(event.currentTarget.files); event.currentTarget.value = ''; }} />
          <button type="button" className="btn btn-ghost" disabled={saving} onClick={() => fileInput.current?.click()}>Add a photo</button>
          {photos.map((photo, index) => <label key={index} className="field">
            <img src={photo.url} alt={`Photo ${index + 1}`} className="photo-preview" />
            Note for photo {index + 1}<input className="input" type="text" maxLength={500} value={photo.note}
              onChange={event => { const value = event.currentTarget.value; setPhotos(current => current.map((item, i) => i === index ? { ...item, note: value } : item)); }} />
          </label>)}
          <label className="field">Reason<input className="input" type="text" maxLength={500} value={photoReason} onChange={event => setPhotoReason(event.currentTarget.value)} /></label>
          {photoError && <p role="alert" className="alert">{photoError}</p>}
          <div className="dlg-foot"><button type="button" className="btn btn-ghost" disabled={saving} onClick={() => setPhotoFor(null)}>Cancel</button><button type="button" className="btn btn-primary" disabled={saving} onClick={() => { void savePhotos(); }}>Save photos</button></div>
      </Dialog>}
    </div>
  );
}
