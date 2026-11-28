import { expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// A Scheduler fixture shaped like the design canvas: Anna, Ville and Minna on Tuesday 15 January 2030.
// Times are Europe/Helsinki, UTC+2 in January.
export const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
export const DAY = '2030-01-15';
export const PAST = '2030-01-14';

const hours = (start: string, end: string) => Object.fromEntries(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'].map(day => [day, { start, end }]));
const visit = (fields: Record<string, unknown>) => ({ status: null, originalStartUtc: null, extraExceptionId: null, exceptionId: null, visitEmployeeId: null,
  assignmentVersion: null, isBranchMain: false, isPartnerBooking: false, moveLockReason: null, reassignLockReason: null, ...fields });

export type SchedulerState = {
  calendarCalls: string[]; scheduleCalls: string[]; saved: { method: string; path: string; body: any }[];
  workSaved: boolean; failWork: number; failVisit: boolean;
};

export async function setupScheduler(page: Page, options: { calendar?: boolean; query?: string; thirdOverlap?: boolean } = {}) {
  await setupAdminMocks(page);
  const state: SchedulerState = { calendarCalls: [], scheduleCalls: [], saved: [], workSaved: false, failWork: 0, failVisit: false };
  const calendar = options.calendar ?? true;
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], canUseShiftCalendar: calendar, employee: null } }));
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const url = new URL(request.url()); const path = url.pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (request.method() !== 'GET') {
      const body = request.postDataJSON();
      state.saved.push({ method: request.method(), path, body });
      if (path.endsWith('/calendar/work-time')) {
        if (state.failWork) return route.fulfill({ status: state.failWork, headers, json: { detail: 'Work times changed. Reload the calendar before saving.' } });
        state.workSaved = true; return route.fulfill({ headers, json: { id: 'work' } });
      }
      if (path.endsWith('/calendar/work-photos')) return route.fulfill({ headers, json: { id: 'photos' } });
      if (path.endsWith('/branch-visits')) {
        if (state.failVisit) return route.fulfill({ status: 409, headers, json: { detail: 'Anna Korhonen already has a visit at this time.' } });
        return route.fulfill({ headers, json: { id: 'new-visit', shiftId: 'kallio', branchId: 'kallio-branch', number: 4, startUtc: `${DAY}T06:00:00Z`, endUtc: `${DAY}T07:00:00Z` } });
      }
      return route.fulfill({ headers, json: {} });
    }
    if (path.endsWith('/shifts/employee-schedule')) {
      state.scheduleCalls.push(url.searchParams.get('from')!.slice(0, 10));
      return route.fulfill({ headers, json: [
        { employeeId: 'anna', localDay: `${PAST}T00:00:00`, visits: [visit({ shiftId: 'kallio', shiftName: 'Kallio, Lindqvist flat', startUtc: `${PAST}T06:00:00Z`, endUtc: `${PAST}T09:00:00Z`,
          companyName: 'Virtanen Oy', branchName: 'Kallio', workStatus: 'Completed', visitNumber: 3, checklistDone: 12, checklistTotal: 12, assigneeIds: ['anna'], rosterVersion: 'r-kallio',
          isBranchMain: true, moveLockReason: 'Started or completed work cannot be moved or reassigned.' })] },
        { employeeId: 'anna', localDay: `${DAY}T00:00:00`, visits: [visit({ shiftId: 'espoo', shiftName: 'Espoo, weekly clean', startUtc: `${DAY}T07:00:00Z`, endUtc: `${DAY}T10:00:00Z`,
          companyName: 'Virtanen Oy', branchName: 'Espoo', workStatus: 'InProgress', checklistDone: 4, checklistTotal: 10, assigneeIds: ['anna'], rosterVersion: 'r-espoo',
          moveLockReason: 'Started or completed work cannot be moved or reassigned.' })] },
        { employeeId: 'ville', localDay: `${DAY}T00:00:00`, visits: [
          visit({ shiftId: 'hietaniemi', shiftName: 'Hietaniemi office', startUtc: `${DAY}T08:00:00Z`, endUtc: `${DAY}T12:00:00Z`, companyName: 'Hietaniemi office Oy', branchName: 'Hietaniemi',
            workStatus: 'Scheduled', visitNumber: 1, checklistDone: 0, checklistTotal: 9, assigneeIds: ['ville'], rosterVersion: 'r-hieta' }),
          visit({ shiftId: 'kallio-ville', shiftName: 'Kallio, Ville R.', startUtc: `${DAY}T10:00:00Z`, endUtc: `${DAY}T12:00:00Z`, companyName: 'Virtanen Oy', branchName: 'Kallio',
            workStatus: 'Scheduled', assigneeIds: ['ville'], rosterVersion: 'r-kv' }),
          ...(options.thirdOverlap ? [visit({ shiftId: 'third', shiftName: 'Third overlapping clean with a long property name', startUtc: `${DAY}T09:00:00Z`, endUtc: `${DAY}T11:00:00Z`,
            companyName: 'Virtanen Oy', branchName: 'Töölö', workStatus: 'Scheduled', assigneeIds: ['ville'], rosterVersion: 'r-third' })] : [])] },
        { employeeId: 'minna', localDay: `${DAY}T00:00:00`, visits: [visit({ shiftId: 'espoo-minna', shiftName: 'Espoo, weekly clean', startUtc: `${DAY}T05:30:00Z`, endUtc: `${DAY}T07:30:00Z`,
          companyName: 'Virtanen Oy', branchName: 'Espoo', workStatus: 'NeedsAction', visitNumber: 3, visitNotes: 'Not started. Call the assignee.', assigneeIds: ['minna'], rosterVersion: 'r-em' })] },
      ] });
    }
    if (path.endsWith('/shifts/overlap-audit')) return route.fulfill({ headers, json: [{ employeeId: 'ville',
      earlier: { shiftId: 'hietaniemi', shiftName: 'Hietaniemi office', startUtc: `${DAY}T08:00:00Z`, endUtc: `${DAY}T12:00:00Z` },
      later: { shiftId: 'kallio-ville', shiftName: 'Kallio, Ville R.', startUtc: `${DAY}T10:00:00Z`, endUtc: `${DAY}T12:00:00Z` } }] });
    if (path.endsWith('/shifts/calendar')) {
      const date = url.searchParams.get('date')!;
      state.calendarCalls.push(date);
      const person = (employeeId: string, name: string, extra: Record<string, unknown> = {}) => ({ employeeId, name, status: 'Not started', actualStartUtc: null, actualEndUtc: null,
        hours: null, version: null, canRecord: date < DAY, review: null, ...extra });
      const occurrences = date === PAST ? [{ shiftId: 'kallio', shiftName: 'Kallio, Lindqvist flat', companyName: 'Virtanen Oy', branchName: 'Kallio', startUtc: `${PAST}T06:00:00Z`, endUtc: `${PAST}T09:00:00Z`,
        exceptionId: null, cancelled: false, ambiguous: false, needsReview: false,
        assignees: [person('anna', 'Anna Korhonen', state.workSaved
          ? { status: 'Recorded', actualStartUtc: `${PAST}T06:15:00Z`, actualEndUtc: `${PAST}T09:00:00Z`, hours: 2.75, version: 'v-2' }
          : { status: 'Recorded', actualStartUtc: `${PAST}T06:05:00Z`, actualEndUtc: `${PAST}T09:02:00Z`, hours: 2.95, version: 'v-1' })] }]
        : date === DAY ? [
          { shiftId: 'espoo', shiftName: 'Espoo, weekly clean', companyName: 'Virtanen Oy', branchName: 'Espoo', startUtc: `${DAY}T07:00:00Z`, endUtc: `${DAY}T10:00:00Z`, exceptionId: null,
            cancelled: false, ambiguous: false, needsReview: true, assignees: [person('anna', 'Anna Korhonen', { status: 'Started', actualStartUtc: `${DAY}T07:02:00Z`,
              review: { kind: 'missing-completion', label: 'Started but never ended' } })] },
          { shiftId: 'hietaniemi', shiftName: 'Hietaniemi office', companyName: 'Hietaniemi office Oy', branchName: 'Hietaniemi', startUtc: `${DAY}T08:00:00Z`, endUtc: `${DAY}T12:00:00Z`, exceptionId: null,
            cancelled: false, ambiguous: false, needsReview: false, assignees: [person('ville', 'Ville Rantanen')] },
          { shiftId: 'espoo-minna', shiftName: 'Espoo, weekly clean', companyName: 'Virtanen Oy', branchName: 'Espoo', startUtc: `${DAY}T05:30:00Z`, endUtc: `${DAY}T07:30:00Z`, exceptionId: null,
            cancelled: false, ambiguous: false, needsReview: true, assignees: [person('minna', 'Minna Salo', { review: { kind: 'missing-completion', label: 'No start or end recorded' } })] },
          { shiftId: 'showroom', shiftName: 'Helsinki showroom', companyName: 'Virtanen Oy', branchName: 'Helsinki showroom', startUtc: `${DAY}T13:00:00Z`, endUtc: `${DAY}T14:00:00Z`, exceptionId: 'cancelled-1',
            cancelled: true, ambiguous: false, needsReview: false, assignees: [person('minna', 'Minna Salo')] },
        ] : [];
      return route.fulfill({ headers, json: { date, today: DAY, timeZoneId: 'Europe/Helsinki', occurrences } });
    }
    if (path.endsWith('/admin/shifts')) return route.fulfill({ headers, json: [
      { id: 'kallio', companyId: 'virtanen', branchId: 'kallio-branch', name: 'Kallio, Lindqvist flat', isActive: true, isBranchMain: true, schedule: {} },
      { id: 'helsinki', companyId: 'virtanen', branchId: 'helsinki-branch', name: 'Helsinki showroom', isActive: true, isBranchMain: true, schedule: {} },
      { id: 'old', companyId: 'virtanen', branchId: 'old-branch', name: 'Closed office', isActive: false, isBranchMain: true, schedule: {} },
      { id: 'weekly', companyId: 'virtanen', branchId: 'weekly-branch', name: 'Weekly contract', isActive: true, isBranchMain: false, schedule: {} },
    ] });
    if (path.endsWith('/employees')) return route.fulfill({ headers, json: [
      { id: 'anna', firstName: 'Anna', lastName: 'Korhonen', role: 'employee', isActive: true, defaultHours: hours('08:00', '16:00') },
      { id: 'ville', firstName: 'Ville', lastName: 'Rantanen', role: 'employee', isActive: true, defaultHours: hours('07:00', '15:00') },
      { id: 'minna', firstName: 'Minna', lastName: 'Salo', role: 'employee', isActive: true, defaultHours: {} },
    ] });
    if (path.endsWith('/tenant')) return route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: [] });
  });
  await page.goto(`/admin/shifts/scheduler/${options.query ?? `?date=${DAY}`}`);
  await expect(page.getByRole('heading', { name: 'Scheduler', exact: true })).toBeVisible();
  return state;
}

export const column = (page: Page, name: string, day = DAY) => page.getByRole('group', { name: `${name}, ${day}` });
export const panel = (page: Page) => page.getByRole('complementary', { name: 'Visit details' });
