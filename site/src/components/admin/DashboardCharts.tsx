'use client';

import { useEffect, useMemo, useState } from 'react';
import { Button, Group, Paper, Select, Stack, Text } from '@mantine/core';
import { authorizedFetch } from '@/lib/adminApi';
import { LineChart, type LineSeries } from '@/components/admin/LineChart';
import { CleanerWeekDonuts } from '@/components/admin/CleanerWeekDonuts';

type CompanyDay = { companyId: string; customerName: string; scheduledVisits: number; scheduledHours: number };
type Day = { date: string; scheduledVisits: number; scheduledHours: number; actualHours: number; companies: CompanyDay[] };
type Month = { month: string; days: Day[] };
type SortKey = 'name' | 'visits' | 'hours';

const SORTS: { value: SortKey; label: string }[] = [
  { value: 'visits', label: 'Most visits' },
  { value: 'hours', label: 'Most scheduled hours' },
  { value: 'name', label: 'Customer name' },
];
const CUSTOMER_COLORS = ['#4f46e5', '#0891b2', '#db2777', '#65a30d'];
/** How many customers get their own line in the visits chart. */
const SHOWN_CUSTOMERS = 4;

function currentMonth() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

function shiftMonth(month: string, delta: number) {
  const [y, m] = month.split('-').map(Number);
  const date = new Date(y, m - 1 + delta, 1);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

function monthTitle(month: string) {
  const [y, m] = month.split('-').map(Number);
  return new Date(y, m - 1, 1).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' });
}

export function DashboardCharts() {
  const [month, setMonth] = useState(currentMonth);
  const [data, setData] = useState<Month | null>(null);
  const [failed, setFailed] = useState(false);
  const [sort, setSort] = useState<SortKey>('visits');

  useEffect(() => {
    let current = true;
    setFailed(false);
    setData(null);
    authorizedFetch(`/api/v1/admin/dashboard/daily?month=${month}`, { cache: 'no-store' })
      .then(response => { if (!response.ok) throw new Error(String(response.status)); return response.json(); })
      .then((value: Month) => {
        if (!value || !Array.isArray(value.days)) throw new Error('unexpected response');
        if (current) setData(value);
      })
      .catch(() => { if (current) setFailed(true); });
    return () => { current = false; };
  }, [month]);

  const days = useMemo(() => data?.days ?? [], [data]);
  const labels = days.map(d => d.date.slice(8, 10).replace(/^0/, ''));

  // Customers for the month, ordered by the chosen sort; the first few get their own line.
  const customers = useMemo(() => {
    const totals = new Map<string, { id: string; name: string; visits: number; hours: number; perDay: Map<string, CompanyDay> }>();
    for (const day of days) for (const company of day.companies) {
      const entry = totals.get(company.companyId) ?? { id: company.companyId, name: company.customerName, visits: 0, hours: 0, perDay: new Map() };
      entry.visits += company.scheduledVisits; entry.hours += company.scheduledHours; entry.perDay.set(day.date, company);
      totals.set(company.companyId, entry);
    }
    return [...totals.values()].sort((a, b) => sort === 'name' ? a.name.localeCompare(b.name)
      : sort === 'hours' ? b.hours - a.hours || a.name.localeCompare(b.name) : b.visits - a.visits || a.name.localeCompare(b.name));
  }, [days, sort]);

  const visitSeries: LineSeries[] = [
    { label: 'All customers', color: '#0b1a3a', strong: true, values: days.map(d => d.scheduledVisits) },
    ...customers.slice(0, SHOWN_CUSTOMERS).map((customer, index) => ({
      label: customer.name, color: CUSTOMER_COLORS[index % CUSTOMER_COLORS.length],
      values: days.map(d => customer.perDay.get(d.date)?.scheduledVisits ?? 0),
    })),
  ];
  const hourSeries: LineSeries[] = [
    { label: 'Scheduled hours', color: '#94a3b8', strong: true, values: days.map(d => d.scheduledHours) },
    { label: 'Actual hours', color: '#16a34a', strong: true, values: days.map(d => d.actualHours) },
  ];
  const hasData = days.some(d => d.scheduledVisits > 0 || d.actualHours > 0);

  const monthSwitcher = <Group gap="xs" wrap="nowrap">
    <Button size="xs" variant="default" aria-label="Previous month" onClick={() => setMonth(shiftMonth(month, -1))}>‹</Button>
    <Text size="sm" fw={600} style={{ minWidth: 150, textAlign: 'center' }}>{monthTitle(month)}</Text>
    <Button size="xs" variant="default" aria-label="Next month" onClick={() => setMonth(shiftMonth(month, 1))}>›</Button>
  </Group>;

  if (failed) return <Stack gap="md">
    <Paper withBorder p="md" radius="md" className="office-card"><Text role="alert" c="red">Charts could not be loaded. Refresh to try again.</Text></Paper>
    <CleanerWeekDonuts />
  </Stack>;
  if (!data) return <Paper withBorder p="md" radius="md" className="office-card"><Text c="dimmed">Loading charts…</Text></Paper>;

  return <Stack gap="md">
    <Paper withBorder p="md" radius="md" className="office-card">
      <Group justify="space-between" mb="sm" wrap="wrap">
        <div>
          <Text fw={650}>Visits per day</Text>
          <Text size="xs" c="dimmed">The bold line is all customers. Lines below are the top customers for the chosen sort.</Text>
        </div>
        <Group gap="sm" wrap="wrap">
          {monthSwitcher}
          <Select aria-label="Sort customers by" size="xs" w={200} allowDeselect={false} value={sort} onChange={value => setSort((value ?? 'visits') as SortKey)} data={SORTS} />
        </Group>
      </Group>
      {!hasData ? <Text c="dimmed" size="sm">No visits in {monthTitle(month)}.</Text>
        : <LineChart labels={labels} labelEvery={days.length > 20 ? 5 : 1} series={visitSeries}
          label={`Visits per day in ${monthTitle(month)}. ${visitSeries.map(s => `${s.label}: ${s.values.reduce((a, b) => a + b, 0)} in total`).join('. ')}`} />}
    </Paper>

    <Paper withBorder p="md" radius="md" className="office-card">
      <Group justify="space-between" mb={4} wrap="wrap">
        <div>
          <Text fw={650}>Scheduled hours vs actual hours per day</Text>
          <Text size="xs" c="dimmed">Scheduled: visit length. Actual: submitted cleaner time. A visit without a valid submitted time adds no actual hours.</Text>
        </div>
        {monthSwitcher}
      </Group>
      {!hasData ? <Text c="dimmed" size="sm">No hours in {monthTitle(month)}.</Text>
        : <LineChart labels={labels} labelEvery={days.length > 20 ? 5 : 1} series={hourSeries}
          label={`Scheduled and actual hours per day in ${monthTitle(month)}. Scheduled hours in total: ${hourSeries[0].values.reduce((a, b) => a + b, 0).toFixed(1)}. Actual hours in total: ${hourSeries[1].values.reduce((a, b) => a + b, 0).toFixed(1)}.`} />}
    </Paper>

    <CleanerWeekDonuts />
  </Stack>;
}
