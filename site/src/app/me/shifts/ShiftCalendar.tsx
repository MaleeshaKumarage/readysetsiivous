'use client';

import { useState, useMemo, type ReactNode } from 'react';
import type { MyShiftOccurrence } from '@/lib/meApi';

// Cache Intl.DateTimeFormat instances to avoid high allocation/initialization overhead on every occurrence loop iteration.
// In JS engines, instantiating Intl.DateTimeFormat repeatedly is expensive (~20x slower than reusing instances).
const dateKeyFormatterCache = new Map<string, Intl.DateTimeFormat>();
const weekdayFormatter = new Intl.DateTimeFormat('en-GB', { weekday: 'long', timeZone: 'UTC' });

function getDateKeyFormatter(zone: string): Intl.DateTimeFormat {
  let fmt = dateKeyFormatterCache.get(zone);
  if (!fmt) {
    fmt = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: zone });
    dateKeyFormatterCache.set(zone, fmt);
  }
  return fmt;
}

function dateKey(instant: string, zone: string) {
  const parts = getDateKeyFormatter(zone).formatToParts(new Date(instant));
  let year = '', month = '', day = '';
  // Single-pass loop over parts avoids 3x array search (parts.find) per date key
  for (let i = 0; i < parts.length; i++) {
    const p = parts[i];
    if (p.type === 'year') year = p.value;
    else if (p.type === 'month') month = p.value;
    else if (p.type === 'day') day = p.value;
  }
  return `${year}-${month}-${day}`;
}

function mondayOf(date: string) {
  const value = new Date(`${date}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() - (value.getUTCDay() + 6) % 7);
  return value.toISOString().slice(0, 10);
}

function label(date: string) {
  const [year, month, day] = date.split('-');
  const weekday = weekdayFormatter.format(new Date(`${date}T12:00:00Z`));
  return `${day}/${month}/${year} ${weekday}`;
}

function weekNumber(monday: string) {
  const thursday = new Date(`${monday}T00:00:00Z`); thursday.setUTCDate(thursday.getUTCDate() + 3);
  return 1 + Math.round((Date.parse(`${monday}T00:00:00Z`) - Date.parse(`${mondayOf(`${thursday.getUTCFullYear()}-01-04`)}T00:00:00Z`)) / 604800000);
}

export default function ShiftCalendar({ occurrences, renderShift, disabled, onCloseForm, view }: {
  occurrences: MyShiftOccurrence[]; renderShift: (shift: MyShiftOccurrence) => ReactNode;
  disabled: boolean; onCloseForm: () => void; view: 'past' | 'future';
}) {
  const [openWeek, setOpenWeek] = useState<string | null>(null);
  const [openDay, setOpenDay] = useState<string | null>(null);

  // Performance Optimization:
  // Pre-group and sort occurrences by week and day using useMemo, and push directly to arrays
  // instead of spreading arrays on every insertion (which is O(K^2)).
  // Also pre-compute date labels and week numbers. This prevents repeating expensive Intl
  // formatting, Date object creation, array spreading, and Date.parse sorting on every UI state change
  // (e.g. toggling openWeek or openDay).
  const groupedWeeks = useMemo(() => {
    const compareDates = (a: string, b: string) => view === 'future' ? a.localeCompare(b) : b.localeCompare(a);
    const weeksMap = new Map<string, Map<string, MyShiftOccurrence[]>>();
    for (const occurrence of occurrences) {
      const day = dateKey(occurrence.startUtc, occurrence.timeZoneId);
      const week = mondayOf(day);
      let daysMap = weeksMap.get(week);
      if (!daysMap) {
        daysMap = new Map();
        weeksMap.set(week, daysMap);
      }
      let dayShifts = daysMap.get(day);
      if (!dayShifts) {
        dayShifts = [];
        daysMap.set(day, dayShifts);
      }
      dayShifts.push(occurrence);
    }

    const sortedWeeks = [...weeksMap.entries()].sort(([a], [b]) => compareDates(a, b));

    return sortedWeeks.map(([week, daysMap]) => {
      const sortedDays = [...daysMap.entries()].sort(([a], [b]) => compareDates(a, b));
      const days = sortedDays.map(([day, shifts]) => {
        const sortedShifts = shifts.slice().sort((a, b) =>
          view === 'future'
            ? Date.parse(a.startUtc) - Date.parse(b.startUtc)
            : Date.parse(b.startUtc) - Date.parse(a.startUtc)
        );
        return {
          day,
          dayLabel: label(day),
          shifts: sortedShifts,
        };
      });
      const totalCount = days.reduce((sum, d) => sum + d.shifts.length, 0);
      return {
        week,
        weekLabel: label(week),
        weekNum: weekNumber(week),
        totalCount,
        days,
      };
    });
  }, [occurrences, view]);

  const selectDay = (week: string, day: string) => {
    onCloseForm();
    setOpenWeek(week);
    setOpenDay(openWeek === week && openDay === day ? null : day);
  };

  return <div className="space-y-4" aria-label="Shifts by week and day">
    {groupedWeeks.map(({ week, weekLabel, weekNum, totalCount, days }) => {
      const expanded = openWeek === week;
      return <section key={week} data-testid={`shift-week-${week}`} className="rounded-xl border border-gray-200 bg-white p-4 dark:border-gray-700 dark:bg-gray-900">
        <div className="flex items-center gap-3 overflow-x-auto pb-1">
          <button type="button" aria-expanded={expanded} aria-controls={`week-${week}`} disabled={disabled}
            aria-label={`Week of ${weekLabel} · ${totalCount} ${totalCount === 1 ? 'shift' : 'shifts'}`}
            className="shrink-0 whitespace-nowrap rounded px-2 py-2 font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            onClick={() => { onCloseForm(); setOpenWeek(expanded ? null : week); setOpenDay(expanded ? null : days[0]?.day ?? null); }}>
            Week {weekNum} · {totalCount} {totalCount === 1 ? 'shift' : 'shifts'} {expanded ? '▴' : '▾'}
          </button>
          <div className="flex shrink-0 gap-2" aria-label="Daily shift counts">
            {days.map(({ day, dayLabel, shifts }) => <button key={day} type="button" disabled={disabled}
              aria-expanded={expanded && openDay === day} aria-controls={`day-${day}`}
              aria-label={`${dayLabel} · ${shifts.length} ${shifts.length === 1 ? 'shift' : 'shifts'}`}
              onClick={() => selectDay(week, day)}
              className="whitespace-nowrap rounded-lg border border-gray-300 px-3 py-2 text-sm focus-visible:outline focus-visible:outline-2 dark:border-gray-600">
              <span>{dayLabel}</span>
              <span className="ml-2 inline-flex rounded-full bg-gray-100 px-2 py-0.5 text-xs font-semibold text-gray-700 dark:bg-gray-800 dark:text-gray-200">{shifts.length} {shifts.length === 1 ? 'shift' : 'shifts'}</span>
            </button>)}
          </div>
        </div>
        <div id={`week-${week}`} hidden={!expanded}>
          {days.map(({ day, shifts }) => <div key={day} id={`day-${day}`} hidden={!expanded || openDay !== day} className="mt-4 space-y-4" role="region" aria-label={`Shifts on ${day}`}>
            {shifts.map(renderShift)}
          </div>)}
        </div>
      </section>;
    })}
  </div>;
}
