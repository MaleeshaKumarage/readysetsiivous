'use client';

import { useEffect, useState } from 'react';
import { Button, Group, Paper, Stack, Text } from '@mantine/core';
import { authorizedFetch } from '@/lib/adminApi';

type CompanyHours = { companyId: string; customerName: string; scheduledHours: number };
type Person = { employeeId: string; name: string; scheduledHours: number; companies: CompanyHours[] };
type Week = { weekStart: string; fullTimeHours: number; employees: Person[] };

const RADIUS = 38;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
const COLORS = ['#0b1a3a', '#c9a24a', '#4f46e5', '#0891b2', '#db2777', '#65a30d', '#94a3b8'];

function shiftWeek(weekStart: string, days: number) {
  const [y, m, d] = weekStart.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

function weekLabel(weekStart: string) {
  const [y, m, d] = weekStart.split('-').map(Number);
  const fmt = (date: Date) => date.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' });
  return `${fmt(new Date(Date.UTC(y, m - 1, d)))} – ${fmt(new Date(Date.UTC(y, m - 1, d + 6)))}`;
}

/** One donut per cleaner: the scheduled hours of the week, split by customer, against a full-time week. */
function Donut({ name, companies, total, full }: { name: string; companies: CompanyHours[]; total: number; full: number }) {
  let offset = 0;
  const over = full > 0 && total > full;
  return <Stack gap={4} align="center">
    <svg viewBox="0 0 100 100" width={120} height={120} role="img" aria-label={`${name}: ${total.toFixed(1)} of ${full} scheduled hours, ${companies.map(c => `${c.customerName} ${c.scheduledHours.toFixed(1)}`).join(', ') || 'no visits'}`}>
      <circle cx="50" cy="50" r={RADIUS} fill="none" stroke="#e5e7eb" strokeWidth="12" />
      {companies.map((company, index) => {
        const share = full > 0 ? company.scheduledHours / full : 0;
        // Segments stop at the full circle, so a cleaner over the full week does not draw over earlier customers.
        const length = Math.max(0, Math.min(CIRCUMFERENCE - offset, CIRCUMFERENCE * share));
        const segment = <circle key={company.companyId} cx="50" cy="50" r={RADIUS} fill="none" stroke={COLORS[index % COLORS.length]} strokeWidth="12"
          strokeDasharray={`${length} ${CIRCUMFERENCE}`} strokeDashoffset={-offset} transform="rotate(-90 50 50)" />;
        offset += length;
        return segment;
      })}
      <text x="50" y="54" textAnchor="middle" fontSize="16" fontWeight="700" fill="#0b1a3a">{total.toFixed(1)}</text>
    </svg>
    <Text size="sm" fw={600} ta="center">{name}</Text>
    <Text size="xs" c={over ? 'orange' : 'dimmed'} ta="center">{total.toFixed(1)} of {full} h{over ? ' · over' : ''}</Text>
    <Stack gap={2} w="100%">
      {companies.length === 0 ? <Text size="xs" c="dimmed" ta="center">No visits this week</Text> : companies.map((company, index) => <Group key={company.companyId} gap={6} wrap="nowrap" justify="space-between">
        <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
          <span aria-hidden="true" style={{ width: 10, height: 10, borderRadius: 3, background: COLORS[index % COLORS.length], display: 'inline-block', flex: '0 0 10px' }} />
          <Text size="xs" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{company.customerName}</Text>
        </Group>
        <Text size="xs" c="dimmed">{company.scheduledHours.toFixed(1)} h</Text>
      </Group>)}
    </Stack>
  </Stack>;
}

export function CleanerWeekDonuts() {
  const [week, setWeek] = useState<Week | null>(null);
  const [requested, setRequested] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let current = true;
    setFailed(false);
    const query = requested ? `?weekStart=${requested}` : '';
    authorizedFetch(`/api/v1/admin/dashboard/employee-hours${query}`, { cache: 'no-store' })
      .then(response => { if (!response.ok) throw new Error(String(response.status)); return response.json(); })
      .then((value: Week) => {
        // Anything other than a week of hours is a failed load, not something to draw.
        if (!value || typeof value.weekStart !== 'string' || !Array.isArray(value.employees)) throw new Error('unexpected response');
        if (current) setWeek(value);
      })
      .catch(() => { if (current) setFailed(true); });
    return () => { current = false; };
  }, [requested]);

  if (failed) return <Paper withBorder p="md" radius="md" className="office-card"><Text role="alert" c="red">Weekly hours could not be loaded. Refresh to try again.</Text></Paper>;
  if (!week) return <Paper withBorder p="md" radius="md" className="office-card"><Text c="dimmed">Loading weekly hours…</Text></Paper>;

  return <Paper withBorder p="md" radius="md" className="office-card">
    <Group justify="space-between" mb="sm" wrap="wrap">
      <div>
        <Text fw={650}>Scheduled hours per cleaner, by customer</Text>
        <Text size="xs" c="dimmed">Week {weekLabel(week.weekStart)} · full time is {week.fullTimeHours} h</Text>
      </div>
      <Group gap="xs">
        <Button size="xs" variant="default" onClick={() => setRequested(shiftWeek(week.weekStart, -7))}>Previous week</Button>
        <Button size="xs" variant="default" onClick={() => setRequested(null)}>This week</Button>
        <Button size="xs" variant="default" onClick={() => setRequested(shiftWeek(week.weekStart, 7))}>Next week</Button>
      </Group>
    </Group>
    {week.employees.length === 0 ? <Text c="dimmed" size="sm">No active cleaners.</Text> : <div role="region" aria-label="Cleaners' weekly hours" tabIndex={0}
      style={{ display: 'flex', flexWrap: 'nowrap', gap: 16, overflowX: 'auto', overflowY: 'hidden', paddingBottom: 6 }}>
      {week.employees.map(person => <div key={person.employeeId} style={{ flex: '0 0 150px' }}>
        <Donut name={person.name} companies={person.companies ?? []} total={person.scheduledHours} full={week.fullTimeHours} />
      </div>)}
    </div>}
  </Paper>;
}
