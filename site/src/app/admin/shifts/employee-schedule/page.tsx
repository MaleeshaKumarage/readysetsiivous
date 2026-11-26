'use client';

import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { Alert, Loader } from '@mantine/core';
import { useMediaQuery } from '@mantine/hooks';
import { Badge, Button, Card, Eyebrow, Heading, MultiSelect, buttonClasses, cx, type BadgeVariant } from '@/ds';
import { adminEmployees, adminShifts, adminTenant, authorizedFetch, type Employee, type EmployeeDaySchedule, type OverlapFinding, type ScheduleVisit } from '@/lib/adminApi';

const DAYS = 7;
// Tall enough that a one-hour visit shows its name, time and status in a narrow week column.
const HOUR_PX = 72;
const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

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

// Work state of a visit, the same values as the shift's Visits list. One badge style per state.
const STATUS: Record<string, { label: string; badge: BadgeVariant; block: string }> = {
  Scheduled: { label: 'Scheduled', badge: 'outline', block: 'bg-white border-gray-200 border-l-accent-900' },
  InProgress: { label: 'In progress', badge: 'gold', block: 'bg-brand-50 border-brand-100 border-l-brand-500' },
  Completed: { label: 'Completed', badge: 'navy', block: 'bg-gray-100 border-gray-200 border-l-accent-900' },
  NeedsAction: { label: 'Needs action', badge: 'danger', block: 'bg-red-50 border-red-200 border-l-red-600' },
};
const statusOf = (visit: ScheduleVisit) => STATUS[visit.workStatus ?? ''] ?? STATUS.Scheduled;

type Placed = { key: string; employeeId: string; day: string; visit: ScheduleVisit; start: number; end: number; overlap: boolean };
type Column = { key: string; label: string; title: string; hours: string; employeeId: string; day: string; visits: Placed[] };

/** Side-by-side lanes for visits that overlap in one column, so none hides another. */
function lanes(items: Placed[]) {
  const sorted = [...items].sort((a, b) => a.start - b.start || a.end - b.end);
  const placed = new Map<string, { lane: number; lanes: number }>();
  let cluster: Placed[] = []; let clusterEnd = -1; let laneEnds: number[] = [];
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

function hoursFor(employee: Employee | undefined, day: string, short = false) {
  const hours = employee?.defaultHours?.[weekdayOf(day)];
  if (!hours?.start || !hours?.end) return short ? 'No hours set' : 'No working hours set';
  const range = `${hours.start.slice(0, 5)}–${hours.end.slice(0, 5)}`;
  return short ? range : `Available ${range}`;
}

function Donut({ done, total }: { done: number; total: number }) {
  const percent = total > 0 ? Math.round((done / total) * 100) : 0;
  return <svg width="40" height="40" viewBox="0 0 40 40" role="img" aria-label={`${done} of ${total} checklist items done`} className="shrink-0 -rotate-90">
    <circle cx="20" cy="20" r="15.9" fill="none" stroke="#f3f4f6" strokeWidth="5" />
    <circle cx="20" cy="20" r="15.9" fill="none" stroke="#0d1428" strokeWidth="5" pathLength={100} strokeDasharray={`${percent} 100`} />
  </svg>;
}

/** Desktop visit details: slides in from the right over the calendar, below the admin header. No backdrop, so other visits stay clickable. */
function SlideOver({ children }: { children: ReactNode }) {
  const [shown, setShown] = useState(false);
  useEffect(() => { const frame = requestAnimationFrame(() => setShown(true)); return () => cancelAnimationFrame(frame); }, []);
  return <aside aria-label="Visit details" style={{ top: 'var(--app-shell-header-offset, 0px)' }}
    className={cx('fixed bottom-0 right-0 z-40 w-[22rem] max-w-full overflow-y-auto border-l border-gray-100 bg-gray-50 p-4 shadow-xl shadow-accent-900/10',
      'transition-transform duration-200 ease-out motion-reduce:transition-none', shown ? 'translate-x-0' : 'translate-x-full')}>
    {children}
  </aside>;
}

export default function EmployeeSchedulePage() {
  const [anchor, setAnchor] = useState(() => isoDate(new Date()));
  const [view, setView] = useState<'day' | 'week'>('day');
  const [chosen, setChosen] = useState<string[] | null>(null);
  const [weekEmployee, setWeekEmployee] = useState<string | null>(null);
  const [schedule, setSchedule] = useState<EmployeeDaySchedule[] | null>(null);
  const [overlaps, setOverlaps] = useState<OverlapFinding[]>([]);
  const [employees, setEmployees] = useState<Employee[] | null>(null);
  const [timeZone, setTimeZone] = useState<string | undefined>(undefined);
  const [zoneError, setZoneError] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [canUseCalendar, setCanUseCalendar] = useState(false);
  // The link's date and employee are read before the first load, so a deep link does not load this week first.
  const [ready, setReady] = useState(false);
  const wide = useMediaQuery('(min-width: 48em)', true);
  // Focus moves into the details when they open and back to the visit that opened them when they close.
  const opener = useRef<HTMLButtonElement | null>(null);
  const closeButton = useRef<HTMLButtonElement>(null);
  const detailsOpen = selectedKey !== null;
  const closeDetails = () => {
    setSelectedKey(null);
    const back = opener.current;
    setTimeout(() => { if (back?.isConnected) back.focus(); }, 0);
  };
  useEffect(() => { if (detailsOpen) closeButton.current?.focus(); }, [detailsOpen]);
  useEffect(() => {
    if (!detailsOpen) return;
    // Escape closes the details, unless something inside already used it (an open dropdown list closes itself first).
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape' && !event.defaultPrevented) closeDetails(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [detailsOpen]);

  useEffect(() => {
    // A conflict link opens one employee's week; a date opens that day or week.
    const params = new URLSearchParams(window.location.search);
    const employee = params.get('employee');
    const day = params.get('date');
    if (isDay(day)) setAnchor(day);
    if (employee) { setWeekEmployee(employee); setView('week'); }
    setReady(true);
  }, []);

  const weekStart = mondayOf(anchor);
  const weekEnd = addDays(weekStart, DAYS - 1);

  useEffect(() => {
    // Times are grouped by the workplace day, so they must use the workplace zone. Without it the page does not render times.
    adminTenant.get()
      .then(tenant => { if (tenant?.timeZoneId) setTimeZone(tenant.timeZoneId); else setZoneError(true); })
      .catch(() => setZoneError(true));
    // Only admins with the shift calendar can record work there, so the link is offered to them only.
    authorizedFetch('/api/v1/me').then(response => response.ok ? response.json() : null)
      .then(identity => setCanUseCalendar(identity?.canUseShiftCalendar === true)).catch(() => setCanUseCalendar(false));
  }, []);

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

  const staff = useMemo(() => (employees ?? []).filter(employee => employee.isActive && employee.role !== 'partner'), [employees]);
  const selectedIds = chosen ?? staff.map(employee => employee.id);
  const oneEmployee = staff.find(employee => employee.id === weekEmployee) ?? staff[0];
  const nameOf = (employee: Employee | undefined) => employee ? `${employee.firstName} ${employee.lastName}` : '';

  const formatTime = (value: string) => new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone }).format(new Date(value));
  const local = (value: string) => {
    const parts = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone })
      .formatToParts(new Date(value));
    const get = (type: string) => parts.find(part => part.type === type)?.value ?? '00';
    return { day: `${get('year')}-${get('month')}-${get('day')}`, minutes: Number(get('hour')) * 60 + Number(get('minute')) };
  };
  const sameVisit = (a: ScheduleVisit, b: ScheduleVisit) => a.shiftId === b.shiftId && a.startUtc === b.startUtc;

  // Each employee's visits on each day of the week, positioned in minutes from local midnight.
  const placedFor = (employeeId: string, day: string): Placed[] =>
    (schedule?.find(row => row.employeeId === employeeId && row.localDay.slice(0, 10) === day)?.visits ?? []).map(visit => {
      const start = local(visit.startUtc);
      const end = local(visit.endUtc);
      const endMinutes = end.day === start.day ? Math.max(end.minutes, start.minutes + 15) : 24 * 60;
      return { key: `${employeeId}|${visit.shiftId}|${visit.startUtc}`, employeeId, day, visit, start: start.minutes, end: endMinutes,
        overlap: overlaps.some(finding => finding.employeeId === employeeId && (sameVisit(finding.earlier, visit) || sameVisit(finding.later, visit))) };
    });

  const columns: Column[] = !timeZone ? [] : view === 'week'
    ? (oneEmployee ? Array.from({ length: DAYS }, (_, index) => {
      const day = addDays(weekStart, index);
      const label = new Date(`${day}T12:00:00`).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
      return { key: day, label, title: label, hours: hoursFor(oneEmployee, day, true), employeeId: oneEmployee.id, day, visits: placedFor(oneEmployee.id, day) };
    }) : [])
    : staff.filter(employee => selectedIds.includes(employee.id)).map(employee => ({
      key: employee.id, label: nameOf(employee), title: nameOf(employee), hours: hoursFor(employee, anchor), employeeId: employee.id, day: anchor,
      visits: placedFor(employee.id, anchor),
    }));
  const allPlaced = columns.flatMap(column => column.visits);
  const selected = allPlaced.find(item => item.key === selectedKey) ?? null;

  // The grid spans the working day, widened to fit any earlier or later visit.
  const firstHour = Math.max(0, Math.min(6, ...allPlaced.map(item => Math.floor(item.start / 60))));
  const lastHour = Math.min(24, Math.max(18, ...allPlaced.map(item => Math.ceil(item.end / 60))));
  const hours = Array.from({ length: lastHour - firstHour + 1 }, (_, index) => firstHour + index);

  const needAction = allPlaced.filter(item => item.visit.workStatus === 'NeedsAction').length;
  const summary = view === 'week'
    ? `${allPlaced.length} ${allPlaced.length === 1 ? 'visit' : 'visits'} this week · ${nameOf(oneEmployee)}`
    : `${allPlaced.length} ${allPlaced.length === 1 ? 'visit' : 'visits'} · ${columns.length} ${columns.length === 1 ? 'employee' : 'employees'} · ${needAction} ${needAction === 1 ? 'needs' : 'need'} action`;
  const heading = view === 'week'
    ? `Week of ${new Date(`${weekStart}T12:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })}`
    : new Date(`${anchor}T12:00:00`).toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });

  const step = (count: number) => { setSelectedKey(null); setAnchor(view === 'week' ? addDays(weekStart, count * DAYS) : addDays(anchor, count)); };
  const today = () => { setSelectedKey(null); setAnchor(isoDate(new Date())); };
  const showWeek = () => { setSelectedKey(null); setWeekEmployee(weekEmployee ?? selectedIds[0] ?? staff[0]?.id ?? null); setView('week'); };
  const showDay = () => { setSelectedKey(null); setView('day'); };
  const showAll = () => { setSelectedKey(null); setChosen(null); setView('day'); };

  if (zoneError) return (
    <div className="space-y-4 p-4">
      <Heading as="h2" size="md">Employee schedule</Heading>
      <Alert color="red" title="Time zone unavailable">Could not load the workplace time zone, so visit times cannot be shown correctly. Reload the page to try again.</Alert>
      <Button variant="outline" size="sm" onClick={() => window.location.reload()}>Reload</Button>
    </div>
  );
  if (!timeZone) return (
    <div className="space-y-4 p-4"><Heading as="h2" size="md">Employee schedule</Heading><Loader /></div>
  );

  const visitButton = (item: Placed, style?: CSSProperties) => {
    const status = statusOf(item.visit);
    const time = `${formatTime(item.visit.startUtc)}–${formatTime(item.visit.endUtc)}`;
    return <button key={item.key} type="button" onClick={event => { opener.current = event.currentTarget; setSelectedKey(item.key); }} aria-pressed={selectedKey === item.key}
      aria-label={`${item.visit.shiftName}, ${time}, ${status.label}${item.overlap ? ', overlaps another visit' : ''}`}
      style={style}
      className={cx('overflow-hidden rounded-xl border border-l-4 px-2 py-1.5 text-left text-gray-900 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500',
        status.block, selectedKey === item.key && 'ring-2 ring-accent-900', style ? 'absolute' : 'w-full')}>
      <span className="block truncate text-xs font-bold leading-tight">{item.visit.shiftName}</span>
      <span className="mt-0.5 block text-[11px] tabular-nums text-gray-700">{time}</span>
      <span className="mt-1 flex flex-wrap items-center gap-1">
        <Badge variant={status.badge} className="!px-1.5 !text-[10px]">{status.label}</Badge>
        {item.overlap && <Badge variant="danger" className="!px-1.5 !text-[10px]">Overlap</Badge>}
      </span>
    </button>;
  };

  const details = selected && (() => {
    const visit = selected.visit;
    const status = statusOf(visit);
    const employee = staff.find(person => person.id === selected.employeeId);
    const minutes = Math.round((Date.parse(visit.endUtc) - Date.parse(visit.startUtc)) / 60000);
    const duration = minutes % 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60} min` : `${minutes / 60} h`;
    const date = new Date(`${selected.day}T12:00:00`).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
    const place = [visit.companyName, visit.branchName].filter(Boolean).join(' · ');
    return <Card padding="sm" className="space-y-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 space-y-2">
          <div className="flex flex-wrap gap-1.5">
            <Badge variant={status.badge}>{status.label}</Badge>
            {selected.overlap && <Badge variant="danger">Overlap</Badge>}
          </div>
          <Heading as="h2" size="sm">{visit.shiftName}</Heading>
          {place && <p className="text-sm text-gray-700">{place}</p>}
        </div>
        <button ref={closeButton} type="button" onClick={closeDetails} aria-label="Close visit details"
          className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full border border-gray-300 text-gray-700 hover:border-brand-300">×</button>
      </div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2.5 text-sm">
        <dt className="text-gray-500">Visit</dt><dd className="font-semibold tabular-nums">{date} · {formatTime(visit.startUtc)}–{formatTime(visit.endUtc)}</dd>
        {visit.visitNumber != null && <><dt className="text-gray-500">Number</dt><dd>Visit {visit.visitNumber}</dd></>}
        <dt className="text-gray-500">Employee</dt><dd className="font-semibold">{nameOf(employee)}</dd>
        <dt className="text-gray-500">Duration</dt><dd>{duration}</dd>
        {visit.address && <><dt className="text-gray-500">Site</dt><dd>{visit.address}</dd></>}
        <dt className="text-gray-500">Checklist</dt>
        <dd className="flex items-center gap-2.5">
          {visit.checklistTotal ? <><Donut done={visit.checklistDone ?? 0} total={visit.checklistTotal} /><span>{visit.checklistDone ?? 0} of {visit.checklistTotal} done</span></>
            : <span className="text-gray-500">No checklist yet</span>}
        </dd>
      </dl>
      {selected.overlap && <p className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-600">This visit overlaps another visit for {nameOf(employee)}. Move or reassign one of them.</p>}
      {visit.visitNotes && <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3 text-sm text-gray-700">
        <p className="mb-1 text-xs font-semibold uppercase tracking-wide">Visit notes</p>{visit.visitNotes}
      </div>}
      {canUseCalendar && <a href={`/admin/shifts/calendar/?date=${selected.day}`} className={cx(buttonClasses('primary', 'md'), 'w-full')}>Open in shift calendar</a>}
    </Card>;
  })();

  return (
    <div className="space-y-4 p-1 sm:p-2">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          <Eyebrow>Employees</Eyebrow>
          <Heading as="h2" size="md" className="mt-1">Employee schedule</Heading>
          <p className="mt-1 text-sm text-gray-700">{heading} · {summary}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {view === 'week'
            ? <>
              <Button variant="outline" size="sm" onClick={() => step(-1)}>Previous week</Button>
              <Button variant="outline" size="sm" onClick={today}>This week</Button>
              <Button variant="outline" size="sm" onClick={() => step(1)}>Next week</Button>
            </>
            : <>
              <Button variant="outline" size="sm" onClick={() => step(-1)}>Previous day</Button>
              <Button variant="outline" size="sm" onClick={today}>Today</Button>
              <Button variant="outline" size="sm" onClick={() => step(1)}>Next day</Button>
            </>}
        </div>
      </header>

      <div className="grid gap-4 sm:grid-cols-[repeat(3,minmax(0,1fr))] lg:grid-cols-[repeat(4,minmax(0,1fr))]">
        <label className="block">
          <span className="mb-1.5 block text-sm font-semibold text-gray-700">{view === 'week' ? 'Week starting' : 'Date'}</span>
          <input type="date" value={view === 'week' ? weekStart : anchor}
            onChange={event => { const value = event.currentTarget.value; if (value) { setSelectedKey(null); setAnchor(view === 'week' ? mondayOf(value) : value); } }}
            className="w-full rounded-xl border border-gray-300 bg-white px-4 py-2.5 text-base text-gray-900 focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20" />
        </label>
        <div>
          <span className="mb-1.5 block text-sm font-semibold text-gray-700">View</span>
          <div className="flex gap-2" role="group" aria-label="View">
            <Button size="sm" variant={view === 'day' ? 'primary' : 'outline'} aria-pressed={view === 'day'} onClick={showDay} className="flex-1">Day</Button>
            <Button size="sm" variant={view === 'week' ? 'primary' : 'outline'} aria-pressed={view === 'week'} onClick={showWeek} className="flex-1">Week</Button>
          </div>
        </div>
        {staff.length > 0 && (view === 'week'
          ? <MultiSelect label="Employee" multiple={false} options={staff.map(employee => ({ value: employee.id, label: nameOf(employee) }))}
            value={oneEmployee ? [oneEmployee.id] : []} onChange={value => { setSelectedKey(null); setWeekEmployee(value[0] ?? null); }}
            hint="Week view shows one employee." />
          : <MultiSelect label="Employees" options={staff.map(employee => ({ value: employee.id, label: nameOf(employee) }))}
            value={selectedIds} onChange={value => { setSelectedKey(null); setChosen(value); }} placeholder="Choose employees" />)}
        {view === 'week' && staff.length > 0 && <div className="flex items-end">
          <Button variant="ghost" size="sm" onClick={showAll}>Show all employees</Button>
        </div>}
      </div>

      {loading && <Loader aria-label="Loading schedule" />}
      {error && <Alert color="red" title="Schedule unavailable"><div className="space-y-2"><p>{error}</p><Button size="sm" variant="outline" onClick={() => setReloadKey(key => key + 1)}>Retry</Button></div></Alert>}
      {!loading && !error && schedule && staff.length === 0 && <p className="text-gray-700">No active employees yet. Add employees, then their visits appear here.</p>}
      {!loading && !error && schedule && staff.length > 0 && schedule.length === 0 && <p className="text-gray-700">No visits this week. Choose another week or check the shift schedules.</p>}
      {!loading && !error && schedule && staff.length > 0 && view === 'day' && columns.length === 0 && <p className="text-gray-700">Choose at least one employee to see their visits.</p>}

      {!loading && !error && schedule && columns.length > 0 && (
        <div>
          <section aria-label="Visits" className="min-w-0">
            {wide ? (
              <div className="overflow-x-auto rounded-2xl border border-gray-100 bg-white">
                <div style={{ minWidth: 56 + columns.length * (view === 'week' ? 120 : 150) }}>
                  <div className="sticky top-0 z-10 grid border-b border-gray-100 bg-white" style={{ gridTemplateColumns: `56px repeat(${columns.length}, minmax(0, 1fr))` }}>
                    <div />
                    {columns.map(column => <div key={column.key} className="min-w-0 border-l border-gray-100 px-3 py-2.5">
                      <p className="truncate text-sm font-semibold text-gray-900">{column.title}</p>
                      <p className="truncate text-xs text-gray-500">{column.hours}</p>
                      <p className="text-xs font-semibold text-brand-600">{column.visits.length} {column.visits.length === 1 ? 'visit' : 'visits'}</p>
                    </div>)}
                  </div>
                  <div className="grid" style={{ gridTemplateColumns: `56px repeat(${columns.length}, minmax(0, 1fr))` }}>
                    <div className="relative" style={{ height: (lastHour - firstHour) * HOUR_PX }} aria-hidden="true">
                      {hours.map(hour => <span key={hour} className="absolute right-2 text-xs tabular-nums text-gray-500"
                        style={{ top: Math.min((lastHour - firstHour) * HOUR_PX - 18, Math.max(2, (hour - firstHour) * HOUR_PX - 8)) }}>{String(hour).padStart(2, '0')}:00</span>)}
                    </div>
                    {columns.map(column => {
                      const layout = lanes(column.visits);
                      return <div key={column.key} role="group" aria-label={`${nameOf(staff.find(e => e.id === column.employeeId))}, ${column.day}`}
                        className="relative border-l border-gray-100"
                        style={{ height: (lastHour - firstHour) * HOUR_PX, backgroundImage: 'repeating-linear-gradient(to bottom, #f3f4f6 0, #f3f4f6 1px, transparent 1px, transparent 72px)' }}>
                        {column.visits.map(item => {
                          const place = layout.get(item.key) ?? { lane: 0, lanes: 1 };
                          const width = 100 / Math.max(1, place.lanes);
                          return visitButton(item, {
                            top: ((item.start - firstHour * 60) / 60) * HOUR_PX + 2,
                            height: Math.max(28, ((item.end - item.start) / 60) * HOUR_PX - 4),
                            left: `calc(${place.lane * width}% + 3px)`, width: `calc(${width}% - 6px)`,
                          });
                        })}
                      </div>;
                    })}
                  </div>
                </div>
              </div>
            ) : (
              <div className="space-y-4">
                {columns.map(column => <div key={column.key} role="group" aria-label={`${nameOf(staff.find(e => e.id === column.employeeId))}, ${column.day}`} className="space-y-2">
                  <div className="flex items-baseline justify-between gap-2">
                    <h3 className="text-sm font-semibold text-gray-900">{column.title}</h3>
                    <span className="text-xs text-gray-500">{column.hours}</span>
                  </div>
                  {column.visits.length === 0 ? <p className="text-sm text-gray-500">No visits</p>
                    : [...column.visits].sort((a, b) => a.start - b.start).map(item => visitButton(item))}
                </div>)}
              </div>
            )}
          </section>

          {details && (wide ? <SlideOver key="details">{details}</SlideOver> : (
            <aside aria-label="Visit details" className="fixed inset-x-0 bottom-0 z-40 max-h-[80vh] overflow-y-auto rounded-t-3xl bg-white p-4 shadow-xl shadow-accent-900/10">
              {details}
            </aside>
          ))}
        </div>
      )}
    </div>
  );
}
