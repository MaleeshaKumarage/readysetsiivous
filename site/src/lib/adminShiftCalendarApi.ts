import { authorizedFetch } from './adminApi';

export interface CalendarReview { kind: 'missing-completion' | 'unusual-duration'; label: string }
export interface CalendarEmployee {
  employeeId: string; name: string; actualStartUtc: string | null; actualEndUtc: string | null;
  hours: number | null; status: string; version: string | null; canRecord: boolean; review?: CalendarReview | null;
}
export interface CalendarVisit {
  shiftId: string; shiftName: string; companyName: string; branchName: string;
  startUtc: string; endUtc: string; exceptionId: string | null; cancelled: boolean; ambiguous: boolean;
  needsReview?: boolean; assignees: CalendarEmployee[];
}
export interface AdminShiftCalendar {
  date: string; today: string; timeZoneId: string; occurrences: CalendarVisit[];
}
export class CalendarAccessError extends Error {}
export class CalendarConnectionError extends Error {}
export async function calendarRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response: Response;
  try {
    response = await authorizedFetch(`/api/v1/admin/shifts/calendar${path}`, { ...init, cache: 'no-store' });
  } catch {
    // A failed save may have reached the server before the connection dropped: do not offer a blind retry.
    // A failed load changed nothing, so it gets a plain reload message.
    const reading = !init.method || init.method === 'GET';
    throw new CalendarConnectionError(reading
      ? 'The calendar could not be loaded. Reload the page to try again.'
      : 'The connection was lost before the server confirmed the request. Reload the calendar to check what was saved before trying again.');
  }
  if (response.status === 403) {
    // The server says how to get access; keep a next step even when it sends no detail.
    const problem = await response.json().catch(() => null);
    const detail = typeof problem?.detail === 'string' ? problem.detail.trim() : '';
    throw new CalendarAccessError(detail || 'Shift calendar access is not enabled for your account. Ask an owner to enable it, then reload.');
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail || 'Could not load or save the calendar. Please retry.');
  }
  return response.json() as Promise<T>;
}
export function calendarLocalInput(utc: string, timeZone: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(new Date(utc));
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
