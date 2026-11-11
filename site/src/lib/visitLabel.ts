// The name of one visit on a property's main shift: "#3 · Mon 21 Apr · Admin panel".
// The address, size and cleaning type are in the shift name, so the visit name only tells visits apart.

const SOURCES: Record<string, string> = { AdminPanel: 'Admin panel', PartnerPortal: 'Partner portal' };

export function visitSourceLabel(source?: string | null): string {
  return (source && SOURCES[source]) || 'Admin panel';
}

function parts(utc: string | number, timeZone: string) {
  const found = new Intl.DateTimeFormat('en-GB', { timeZone, weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' }).formatToParts(new Date(utc));
  const get = (type: string) => found.find(part => part.type === type)?.value ?? '';
  return { weekday: get('weekday'), day: get('day'), month: get('month'), year: get('year') };
}

/** One day reads "Mon 21 Apr"; several days read "21–25 Apr" or "30 Apr – 2 May". Dates are in the tenant's time zone. */
export function visitDates(startUtc: string, endUtc: string, timeZone: string): string {
  const first = parts(startUtc, timeZone);
  // A visit ending at midnight belongs to the day before.
  const last = parts(Date.parse(endUtc) - 1, timeZone);
  if (first.day === last.day && first.month === last.month && first.year === last.year) return `${first.weekday} ${first.day} ${first.month}`;
  return first.month === last.month && first.year === last.year
    ? `${first.day}–${last.day} ${last.month}`
    : `${first.day} ${first.month} – ${last.day} ${last.month}`;
}

export function visitLabel(visit: { visitNumber?: number | null; visitSource?: string | null; startUtc: string; endUtc: string }, timeZone: string): string | null {
  if (!visit.visitNumber) return null;
  return `#${visit.visitNumber} · ${visitDates(visit.startUtc, visit.endUtc, timeZone)} · ${visitSourceLabel(visit.visitSource)}`;
}
