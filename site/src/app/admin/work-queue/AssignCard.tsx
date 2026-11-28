'use client';

import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import { adminBookings, adminShifts, type Employee } from '@/lib/adminApi';
import { addDays, dayName, hh, hs, lenText, localOf, personName, snap } from './model';
import { Info } from './Info';

/** When the work happens and what the office may change about it. */
export interface Plan {
  days: string[];
  day: string;
  /** Decimal start hour; null for work with a day but no time (key requests). */
  start: number | null;
  len: number;
  canMove: boolean;
  canResize: boolean;
  /** Shown under the bar when the time cannot be changed here. */
  fixedNote?: string;
}
export interface Slot { day: string; start: number | null; len: number }
type Busy = [number, number, string][];

export interface AssignCardProps {
  plan: Plan;
  employees: Employee[];
  zone: string;
  pickLabel: string;
  help: string;
  verb: string;
  autoAcceptNote?: boolean;
  preselect?: string | null;
  /** "Originally 24 Nov 2026 08:00, 3 h." or "Now Fri 4 Dec 09:00–12:00." */
  original: string;
  saveWord: string;
  busy: boolean;
  onAssign: (employeeId: string, slot: Slot) => void;
}

const pos = (a: number, b: number) => ({ left: `${(Math.max(a, 0) / 24 * 100).toFixed(2)}%`, width: `${((Math.min(b, 24) - Math.max(a, 0)) / 24 * 100).toFixed(2)}%` });

/** The chosen employee's day as a 24-hour bar, with the work as a block to drag, stretch or move to another day. */
export function AssignCard(props: AssignCardProps) {
  const { plan, employees, zone } = props;
  const [day, setDay] = useState(plan.day);
  const [start, setStart] = useState(plan.start);
  const [len, setLen] = useState(plan.len);
  const [pick, setPick] = useState<string | null>(null);
  const [busyMap, setBusyMap] = useState<Map<string, Busy> | null>(null);
  const [loadError, setLoadError] = useState(false);
  const bar = useRef<HTMLDivElement>(null);
  const drag = useRef<{ mode: 'move' | 'end'; offset: number } | null>(null);
  const dragged = useRef(false);

  // Everyone's booked work over the days this item can move to: company visits and assigned public bookings.
  const first = plan.days[0], last = plan.days[plan.days.length - 1];
  useEffect(() => {
    let live = true;
    setBusyMap(null); setLoadError(false);
    Promise.all([adminShifts.employeeSchedule(first, addDays(last, 1)), adminBookings.list(`?fromUtc=${first}T00:00:00Z&toUtc=${addDays(last, 1)}T23:59:59Z`)])
      .then(([schedule, bookings]) => {
        if (!live) return;
        if (!schedule) setLoadError(true);
        const map = new Map<string, Busy>();
        const add = (employeeId: string, startUtc: string, endUtc: string, label: string) => {
          const from = localOf(startUtc, zone), to = localOf(endUtc, zone);
          const key = `${employeeId}|${from.day}`;
          const end = to.day === from.day ? to.hour : 24;
          map.set(key, [...(map.get(key) ?? []), [from.hour, Math.max(from.hour + 0.25, end), label]]);
        };
        for (const row of schedule ?? []) for (const visit of row.visits) {
          if (visit.offerPending || visit.workStatus === 'Offered') continue;
          add(row.employeeId, visit.startUtc, visit.endUtc, visit.branchName ?? visit.companyName ?? visit.shiftName);
        }
        for (const booking of bookings ?? []) {
          if (!booking.employeeId || !booking.startUtc || !booking.endUtc || ['Cancelled', 'Completed'].includes(booking.status)) continue;
          add(booking.employeeId, booking.startUtc, booking.endUtc, `Booking ${booking.bookingNumber}`);
        }
        setBusyMap(map);
      })
      .catch(() => { if (live) { setLoadError(true); setBusyMap(new Map()); } });
    return () => { live = false; };
  }, [first, last, zone]);

  const rows = useMemo(() => employees.map(employee => {
    const blocks = [...(busyMap?.get(`${employee.id}|${day}`) ?? [])].sort((a, b) => a[0] - b[0]);
    const hours = blocks.reduce((total, b) => total + (b[1] - b[0]), 0);
    const hit = start === null ? undefined : blocks.find(b => b[0] < start + len && b[1] > start);
    return { employee, name: personName(employee), blocks, hours: Math.round(hours * 100) / 100, hit };
  }).sort((a, b) => Number(!!a.hit) - Number(!!b.hit) || a.hours - b.hours || a.name.localeCompare(b.name)), [employees, busyMap, day, start, len]);
  const best = rows.find(row => !row.hit)?.employee.id;
  const chosenId = pick && rows.some(row => row.employee.id === pick) ? pick
    : props.preselect && rows.some(row => row.employee.id === props.preselect) ? props.preselect : best ?? rows[0]?.employee.id;
  const chosen = rows.find(row => row.employee.id === chosenId);

  const di = plan.days.indexOf(day);
  const hasSlot = start !== null;
  const clamp = (h: number) => Math.max(0, Math.min(24 - len, snap(h)));
  const fit = (l: number) => Math.max(0.25, Math.min(24 - (start ?? 0), snap(l)));
  const hourAt = (clientX: number) => { const r = bar.current!.getBoundingClientRect(); return ((clientX - r.left) / r.width) * 24; };
  const moved = hasSlot && (day !== plan.day || start !== plan.start || len !== plan.len);
  const timeText = hasSlot ? `${hh(start)}–${hh(start + len)}` : '';

  const onSlotDown = (event: PointerEvent<HTMLSpanElement>, mode: 'move' | 'end') => {
    if (mode === 'move' ? !plan.canMove : !plan.canResize) return;
    event.preventDefault(); event.stopPropagation();
    (event.currentTarget as Element).setPointerCapture?.(event.pointerId);
    drag.current = { mode, offset: mode === 'move' ? hourAt(event.clientX) - (start ?? 0) : 0 };
    dragged.current = false;
  };
  const onSlotMove = (event: PointerEvent<HTMLSpanElement>) => {
    const state = drag.current; if (!state || start === null) return;
    dragged.current = true;
    if (state.mode === 'move') setStart(clamp(hourAt(event.clientX) - state.offset)); else setLen(fit(hourAt(event.clientX) - start));
    setPick(chosenId ?? null);
  };
  const onSlotUp = () => { drag.current = null; };
  const onBarClick = (event: React.MouseEvent<HTMLDivElement>) => {
    if (dragged.current) { dragged.current = false; return; }
    if (!plan.canMove || start === null || event.target !== event.currentTarget) return;
    setStart(clamp(hourAt(event.clientX) - len / 2));
  };
  const nudge = (event: KeyboardEvent, mode: 'move' | 'end') => {
    const step = event.key === 'ArrowLeft' ? -0.25 : event.key === 'ArrowRight' ? 0.25 : 0;
    if (!step || start === null || (mode === 'move' ? !plan.canMove : !plan.canResize)) return;
    event.preventDefault();
    if (mode === 'move') setStart(clamp(start + step)); else setLen(fit(len + step));
  };
  const goDay = (to: number) => { if (to < 0 || to >= plan.days.length) return; setDay(plan.days[to]); if (chosenId) setPick(chosenId); };

  const status = (row: (typeof rows)[number] | undefined) => !row ? '' : row.hit ? `Overlaps ${hh(row.hit[0])}–${hh(row.hit[1])}` : hasSlot ? 'Free' : row.blocks.length ? 'Working' : 'Free all day';
  const describe = (row: (typeof rows)[number]) => row.blocks.length
    ? `${row.blocks.length} ${row.blocks.length === 1 ? 'visit' : 'visits'}, ${row.hours} h booked: ${row.blocks.map(b => `${hh(b[0])}–${hh(b[1])} ${b[2]}`).join(', ')}`
    : 'Nothing booked';
  const whenText = `${dayName(day)}${hasSlot ? ` ${timeText}` : ''}`;
  const clash = chosen?.hit ? `${chosen.name} already has ${chosen.hit[2]} ${hh(chosen.hit[0])}–${hh(chosen.hit[1])}. Drag the block to a free time, try another day, or go ahead anyway and the Scheduler shows both as an overlap.` : '';

  return (
    <div className="assign">
      <div className="row" style={{ justifyContent: 'space-between', alignItems: 'baseline' }}>
        <span className="row" style={{ gap: 4 }}><strong style={{ fontSize: 15 }}>{props.pickLabel}</strong><Info text={props.help} /></span>
        <a href={`/admin/shifts/scheduler/?date=${day}`} style={{ fontSize: 14 }}>Open this day in the Scheduler</a>
      </div>
      {employees.length === 0 ? <p className="note">There are no active employees to choose.</p> : (
        <div className="pp" data-testid="assign-card">
          <div className="pp-pick">
            <label className="field" style={{ flex: '1 1 220px', minWidth: 0 }}>Employee
              <select className="input" value={chosenId ?? ''} onChange={event => setPick(event.target.value)}>
                {rows.map(row => <option key={row.employee.id} value={row.employee.id}>{`${row.name} · ${status(row)}${row.employee.id === best ? ' · Best fit' : ''} · ${row.hours} h booked`}</option>)}
              </select>
            </label>
            <span className="row" style={{ minHeight: 44 }}>
              {chosenId === best && <span className="chip chip-best">Best fit</span>}
              {busyMap && <span className={`chip ${chosen?.hit ? 'chip-busy' : 'chip-free'}`}>{status(chosen)}</span>}
            </span>
          </div>
          <span className="pp-sub">{!busyMap ? 'Loading availability…' : loadError ? 'Availability could not be loaded. You can still assign.' : chosen ? describe(chosen) + (props.autoAcceptNote && chosen.employee.autoAcceptWork ? ' · Accepts automatically' : '') : ''}</span>
          <div className="pp-day">
            <button type="button" className="day-btn" aria-label={`Previous day for ${chosen?.name ?? 'the employee'}`} disabled={di <= 0} onClick={() => goDay(di - 1)}><svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true"><path d="M10 3 5 8l5 5" fill="none" stroke="currentColor" strokeWidth="2" /></svg></button>
            <strong>{dayName(day)}</strong>
            <button type="button" className="day-btn" aria-label={`Next day for ${chosen?.name ?? 'the employee'}`} disabled={di < 0 || di >= plan.days.length - 1} onClick={() => goDay(di + 1)}><svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true"><path d="m6 3 5 5-5 5" fill="none" stroke="currentColor" strokeWidth="2" /></svg></button>
          </div>
          <div ref={bar} className={`bar${plan.canMove ? '' : ' fixed'}`} aria-label={`${chosen?.name ?? 'Employee'} on ${dayName(day)}, 00:00 to 24:00`} onClick={onBarClick} data-testid="day-bar">
            {chosen?.blocks.map((b, i) => <span key={i} className="b-busy" style={pos(b[0], b[1])} title={`${hh(b[0])}–${hh(b[1])} ${b[2]}`} />)}
            {hasSlot && <>
              <span className={chosen?.hit ? 'b-clash' : 'b-slot'} style={pos(start, start + len)} role="slider" tabIndex={0}
                aria-label={`Time for ${chosen?.name ?? 'the work'}.${plan.canMove ? ' Arrow keys move it by 15 minutes.' : ''}`} aria-valuetext={timeText}
                aria-valuemin={0} aria-valuemax={24} aria-valuenow={start} aria-readonly={!plan.canMove} title={timeText}
                onPointerDown={event => onSlotDown(event, 'move')} onPointerMove={onSlotMove} onPointerUp={onSlotUp} onPointerCancel={onSlotUp}
                onKeyDown={event => nudge(event, 'move')}>{hs(start)}–{hs(start + len)}</span>
              {plan.canResize && <span className="b-end" style={{ left: `${((start + len) / 24 * 100).toFixed(2)}%` }} role="slider" tabIndex={0}
                aria-label={`End of the visit, ${chosen?.name ?? 'the employee'}. Arrow keys make it 15 minutes longer or shorter.`} aria-valuetext={lenText(len)}
                aria-valuemin={0.25} aria-valuemax={24} aria-valuenow={len} title="Drag to change the duration"
                onPointerDown={event => onSlotDown(event, 'end')} onPointerMove={onSlotMove} onPointerUp={onSlotUp} onPointerCancel={onSlotUp}
                onKeyDown={event => nudge(event, 'end')} />}
            </>}
          </div>
          <div className="scale" aria-hidden="true"><span>00</span><span>06</span><span>12</span><span>18</span><span>24</span></div>
          {plan.fixedNote && <p className="note">{plan.fixedNote}</p>}
          {moved && <p className="note" data-testid="moved-note">{props.original} The new time, {whenText}, is saved when you {props.saveWord}.</p>}
          {clash && <p role="status" className="clash">{clash}</p>}
          <button type="button" className="btn btn-primary" style={{ alignSelf: 'flex-start', maxWidth: '100%', whiteSpace: 'normal', textAlign: 'left' }}
            disabled={props.busy || !chosenId} onClick={() => chosenId && props.onAssign(chosenId, { day, start, len })}>
            {`${props.verb} ${chosen?.name ?? ''}${clash ? ' anyway' : ''}`.trim()}
          </button>
        </div>
      )}
    </div>
  );
}
