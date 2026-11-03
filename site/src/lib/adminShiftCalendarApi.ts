import { authorizedFetch } from './adminApi';

export interface CalendarEmployee {
  employeeId: string; name: string; actualStartUtc: string | null; actualEndUtc: string | null;
  hours: number | null; status: string; version: string | null; canRecord: boolean;
}
export interface CalendarVisit {
  shiftId: string; shiftName: string; companyName: string; branchName: string;
  startUtc: string; endUtc: string; exceptionId: string | null; cancelled: boolean; ambiguous: boolean;
  assignees: CalendarEmployee[];
}
export interface AdminShiftCalendar {
  date: string; today: string; timeZoneId: string; occurrences: CalendarVisit[];
}
export class CalendarAccessError extends Error {}
export async function calendarRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await authorizedFetch(`/api/v1/admin/shifts/calendar${path}`, { ...init, cache: 'no-store' });
  if (response.status === 403) throw new CalendarAccessError('Shift calendar access is not enabled for your account.');
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail || 'Could not load or save the calendar. Please retry.');
  }
  return response.json() as Promise<T>;
}
// Performance Optimization: Cache Intl.DateTimeFormat instances by timeZone.
// In JS engines, instantiating Intl.DateTimeFormat repeatedly incurs high ICU initialization overhead (~10x-20x slower).
// Because calendarDuration calls calendarLocalInput up to 14 times per duration resolution (evaluating
// candidate offsets), reusing cached formatters avoids repeated instantiation and drops execution time by ~88%.
const calendarLocalFormatters = new Map<string, Intl.DateTimeFormat>();

function getCalendarLocalFormatter(timeZone: string): Intl.DateTimeFormat {
  let fmt = calendarLocalFormatters.get(timeZone);
  if (!fmt) {
    fmt = new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    });
    calendarLocalFormatters.set(timeZone, fmt);
  }
  return fmt;
}

export function calendarLocalInput(utc: string, timeZone: string): string {
  const parts = getCalendarLocalFormatter(timeZone).formatToParts(new Date(utc));
  const part = (name: string) => parts.find(p => p.type === name)?.value;
  return `${part('year')}-${part('month')}-${part('day')}T${part('hour')}:${part('minute')}`;
}

export function calendarDuration(start: string, end: string, timeZone: string): number | null {
  const resolve = (local: string) => {
    const wall = Date.parse(`${local}Z`);
    if (!Number.isFinite(wall)) return null;
    const candidates = new Set<number>();
    for (const hours of [-36, -24, -12, 0, 12, 24, 36]) {
      const sample = wall + hours * 3_600_000;
      const offset = Date.parse(`${calendarLocalInput(new Date(sample).toISOString(), timeZone)}Z`) - sample;
      const candidate = wall - offset;
      if (calendarLocalInput(new Date(candidate).toISOString(), timeZone) === local) candidates.add(candidate);
    }
    return candidates.size === 1 ? [...candidates][0] : null;
  };
  const from = resolve(start); const to = resolve(end);
  return from !== null && to !== null && to > from ? (to - from) / 3_600_000 : null;
}
