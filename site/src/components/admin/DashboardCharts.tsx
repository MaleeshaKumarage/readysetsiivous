'use client';

import { useEffect, useMemo, useState } from 'react';
import { Group, Paper, Select, Stack, Text } from '@mantine/core';
import { authorizedFetch } from '@/lib/adminApi';

type Customer = { companyId: string; customerName: string; scheduledVisits: number; completedVisits: number; scheduledHours: number; actualHours: number };
type Month = { month: string; customers: Customer[] };
type SortKey = 'name' | 'visits' | 'hours';

const SORTS: { value: SortKey; label: string }[] = [
  { value: 'visits', label: 'Most visits' },
  { value: 'hours', label: 'Most scheduled hours' },
  { value: 'name', label: 'Customer name' },
];

function sortCustomers(customers: Customer[], key: SortKey) {
  return [...customers].sort((a, b) => key === 'name' ? a.customerName.localeCompare(b.customerName)
    : key === 'hours' ? b.scheduledHours - a.scheduledHours || a.customerName.localeCompare(b.customerName)
    : b.scheduledVisits - a.scheduledVisits || a.customerName.localeCompare(b.customerName));
}

function monthLabel(month: string) {
  const [year, value] = month.split('-').map(Number);
  return new Date(year, value - 1, 1).toLocaleDateString('en-GB', { month: 'short', year: 'numeric' });
}

/** Bar: a width relative to the largest value on the same chart, so bars are comparable within a chart. */
function Bar({ value, max, color, label }: { value: number; max: number; color: string; label: string }) {
  const width = max > 0 ? Math.max(value > 0 ? 2 : 0, (value / max) * 100) : 0;
  return <div role="img" aria-label={label} style={{ height: 10, background: '#eef1f6', borderRadius: 4, overflow: 'hidden', flex: 1 }}>
    <div style={{ width: `${width}%`, height: '100%', background: color }} />
  </div>;
}

export function DashboardCharts() {
  const [months, setMonths] = useState<Month[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [sort, setSort] = useState<SortKey>('visits');

  useEffect(() => {
    authorizedFetch('/api/v1/admin/dashboard/monthly?months=6', { cache: 'no-store' })
      .then(response => { if (!response.ok) throw new Error(String(response.status)); return response.json(); })
      .then((value: Month[]) => setMonths(value))
      .catch(() => setFailed(true));
  }, []);

  const latest = useMemo(() => months ?? [], [months]);
  const maxVisits = Math.max(1, ...latest.flatMap(m => m.customers.map(c => c.scheduledVisits)));
  // One scale for every month's hours bars, so a longer month reads as longer.
  const maxMonthHours = Math.max(1, ...latest.map(m => Math.max(m.customers.reduce((sum, c) => sum + c.scheduledHours, 0), m.customers.reduce((sum, c) => sum + c.actualHours, 0))));

  if (failed) return <Paper withBorder p="md" radius="md"><Text role="alert" c="red">Charts could not be loaded. Refresh to try again.</Text></Paper>;
  if (!months) return <Paper withBorder p="md" radius="md"><Text c="dimmed">Loading charts…</Text></Paper>;

  return <Stack gap="md">
    <Paper withBorder p="md" radius="md">
      <Group justify="space-between" mb="sm" wrap="wrap">
        <Text fw={650}>Visits per month, by customer</Text>
        <Select aria-label="Sort customers by" size="xs" w={200} allowDeselect={false} value={sort} onChange={value => setSort((value ?? 'visits') as SortKey)} data={SORTS} />
      </Group>
      {latest.length === 0 || latest.every(m => m.customers.length === 0) ? <Text c="dimmed" size="sm">No visits in the last six months.</Text> : latest.map(month => <div key={month.month} style={{ marginBottom: 14 }}>
        <Text size="sm" fw={600} mb={4}>{monthLabel(month.month)}</Text>
        {month.customers.length === 0 ? <Text size="xs" c="dimmed">No visits</Text> : sortCustomers(month.customers, sort).map(customer => <Group key={customer.companyId} gap="sm" wrap="nowrap" mb={4}>
          <Text size="xs" w={180} style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{customer.customerName}</Text>
          <Bar value={customer.scheduledVisits} max={maxVisits} color="#4f46e5" label={`${customer.customerName}: ${customer.scheduledVisits} visits in ${monthLabel(month.month)}`} />
          <Text size="xs" w={60} ta="right">{customer.scheduledVisits} · {customer.completedVisits} done</Text>
        </Group>)}
      </div>)}
    </Paper>

    <Paper withBorder p="md" radius="md">
      <Text fw={650} mb="sm">Scheduled hours vs actual hours</Text>
      <Text size="xs" c="dimmed" mb="sm">Scheduled: visit length. Actual: submitted cleaner time. A visit without a valid submitted time adds no actual hours.</Text>
      {latest.map(month => {
        const scheduled = month.customers.reduce((sum, c) => sum + c.scheduledHours, 0);
        const actual = month.customers.reduce((sum, c) => sum + c.actualHours, 0);
        const variance = scheduled > 0 ? Math.round(((actual - scheduled) / scheduled) * 100) : null;
        return <div key={month.month} style={{ marginBottom: 12 }}>
          <Group justify="space-between" mb={4}><Text size="sm" fw={600}>{monthLabel(month.month)}</Text>
            <Text size="xs" c="dimmed">{variance === null ? 'No scheduled hours' : `${variance >= 0 ? '+' : ''}${variance}% actual vs scheduled`}</Text></Group>
          <Group gap="sm" wrap="nowrap" mb={2}><Text size="xs" w={70}>Scheduled</Text><Bar value={scheduled} max={maxMonthHours} color="#94a3b8" label={`Scheduled hours ${monthLabel(month.month)}: ${scheduled.toFixed(1)}`} /><Text size="xs" w={60} ta="right">{scheduled.toFixed(1)} h</Text></Group>
          <Group gap="sm" wrap="nowrap"><Text size="xs" w={70}>Actual</Text><Bar value={actual} max={maxMonthHours} color="#16a34a" label={`Actual hours ${monthLabel(month.month)}: ${actual.toFixed(1)}`} /><Text size="xs" w={60} ta="right">{actual.toFixed(1)} h</Text></Group>
        </div>;
      })}
    </Paper>
  </Stack>;
}
