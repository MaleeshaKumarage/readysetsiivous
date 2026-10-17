'use client';

import { useState, type ReactNode } from 'react';
import type { MyShiftOccurrence } from '@/lib/meApi';

function dateKey(instant: string, zone: string) {
  const parts = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: zone }).formatToParts(new Date(instant));
  return `${parts.find(p => p.type === 'year')!.value}-${parts.find(p => p.type === 'month')!.value}-${parts.find(p => p.type === 'day')!.value}`;
}

function mondayOf(date: string) {
  const value = new Date(`${date}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() - (value.getUTCDay() + 6) % 7);
  return value.toISOString().slice(0, 10);
}

function label(date: string) {
  return new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${date}T12:00:00Z`));
}

function weekNumber(monday: string) {
  const thursday = new Date(`${monday}T00:00:00Z`); thursday.setUTCDate(thursday.getUTCDate() + 3);
  return 1 + Math.round((Date.parse(`${monday}T00:00:00Z`) - Date.parse(`${mondayOf(`${thursday.getUTCFullYear()}-01-04`)}T00:00:00Z`)) / 604800000);
}

export default function ShiftCalendar({ occurrences, renderShift, disabled, onCloseForm }: {
  occurrences: MyShiftOccurrence[]; renderShift: (shift: MyShiftOccurrence) => ReactNode;
  disabled: boolean; onCloseForm: () => void;
}) {
  const [openWeek, setOpenWeek] = useState<string | null>(null);
  const [openDay, setOpenDay] = useState<string | null>(null);
  const weeks = new Map<string, Map<string, MyShiftOccurrence[]>>();
  for (const occurrence of occurrences) {
    const day = dateKey(occurrence.startUtc, occurrence.timeZoneId);
    const week = mondayOf(day);
    if (!weeks.has(week)) weeks.set(week, new Map());
    const days = weeks.get(week)!;
    days.set(day, [...(days.get(day) ?? []), occurrence]);
  }
  const selectDay = (week: string, day: string) => {
    onCloseForm();
    setOpenWeek(week);
    setOpenDay(openWeek === week && openDay === day ? null : day);
  };
  return <div className="space-y-4" aria-label="Shifts by week and day">
    {[...weeks.entries()].sort(([a], [b]) => b.localeCompare(a)).map(([week, days]) => {
      const ordered = [...days.entries()].sort(([a], [b]) => b.localeCompare(a));
      const expanded = openWeek === week;
      const count = ordered.reduce((sum, [, shifts]) => sum + shifts.length, 0);
      return <section key={week} data-testid={`shift-week-${week}`} className="rounded-xl border border-gray-200 bg-white p-4 dark:border-gray-700 dark:bg-gray-900">
        <div className="flex items-center gap-3 overflow-x-auto pb-1">
          <button type="button" aria-expanded={expanded} aria-controls={`week-${week}`} disabled={disabled}
            aria-label={`Week of ${label(week)} · ${count} ${count === 1 ? 'shift' : 'shifts'}`}
            className="shrink-0 whitespace-nowrap rounded px-2 py-2 font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            onClick={() => { onCloseForm(); setOpenWeek(expanded ? null : week); setOpenDay(expanded ? null : ordered[0][0]); }}>
            Week {weekNumber(week)} · {count} {count === 1 ? 'shift' : 'shifts'} {expanded ? '▴' : '▾'}
          </button>
          <div className="flex shrink-0 gap-2" aria-label="Daily shift counts">
            {ordered.map(([day, shifts]) => <button key={day} type="button" disabled={disabled}
              aria-expanded={expanded && openDay === day} aria-controls={`day-${day}`}
              aria-label={`${label(day)} · ${shifts.length} ${shifts.length === 1 ? 'shift' : 'shifts'}`}
              onClick={() => selectDay(week, day)}
              className="whitespace-nowrap rounded-lg border border-gray-300 px-3 py-2 text-sm focus-visible:outline focus-visible:outline-2 dark:border-gray-600">
              {new Intl.DateTimeFormat(undefined, { weekday: 'short', day: 'numeric', timeZone: 'UTC' }).format(new Date(`${day}T12:00:00Z`))} · {shifts.length}
            </button>)}
          </div>
        </div>
        <div id={`week-${week}`} hidden={!expanded}>
          {ordered.map(([day, shifts]) => <div key={day} id={`day-${day}`} hidden={!expanded || openDay !== day} className="mt-4 space-y-4" role="region" aria-label={`Shifts on ${day}`}>
            {shifts.slice().sort((a, b) => b.startUtc.localeCompare(a.startUtc)).map(renderShift)}
          </div>)}
        </div>
      </section>;
    })}
  </div>;
}
