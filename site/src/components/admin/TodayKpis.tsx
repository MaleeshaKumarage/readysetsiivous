'use client';

import { useCallback, useEffect, useState } from 'react';
import { Paper, SimpleGrid, Text } from '@mantine/core';
import { adminWorkQueue, authorizedFetch } from '@/lib/adminApi';

type DailyDay = { date: string; scheduledVisits: number; scheduledHours: number };

// Local calendar date as yyyy-MM-dd, the same format the daily dashboard data uses.
function todayKey(now: Date) {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** Three figures for the current day: visits in progress now, and the visits and hours scheduled today. */
export function TodayKpis() {
  // Each figure loads on its own, so a failed request shows on its own tile instead of hiding the others.
  const [inProgress, setInProgress] = useState<number | null>(null);
  const [inProgressFailed, setInProgressFailed] = useState(false);
  const [today, setToday] = useState<DailyDay | null>(null);
  const [todayFailed, setTodayFailed] = useState(false);

  const load = useCallback(async () => {
    const now = new Date();
    const key = todayKey(now);
    setInProgressFailed(false); setTodayFailed(false);
    adminWorkQueue.page({ view: 'needs-action', source: 'all', skip: 0, take: 1 })
      .then(work => { if (!work?.counts) throw new Error('no counts'); setInProgress(work.counts['in-progress'] ?? 0); })
      .catch(() => { setInProgress(null); setInProgressFailed(true); });
    authorizedFetch(`/api/v1/admin/dashboard/daily?month=${key.slice(0, 7)}`, { cache: 'no-store' })
      .then(response => { if (!response.ok) throw new Error(String(response.status)); return response.json(); })
      .then((daily: { days?: DailyDay[] }) => {
        if (!Array.isArray(daily?.days)) throw new Error('unexpected response');
        setToday(daily.days.find(day => day.date === key) ?? { date: key, scheduledVisits: 0, scheduledHours: 0 });
      })
      .catch(() => { setToday(null); setTodayFailed(true); });
  }, []);

  useEffect(() => { void load(); }, [load]);

  const tiles = [
    { label: 'Visits in progress now', value: inProgress, failed: inProgressFailed, note: 'Started and not yet ended' },
    { label: 'Scheduled visits today', value: today?.scheduledVisits, failed: todayFailed, note: 'Visits planned for today' },
    { label: 'Scheduled hours today', value: today ? today.scheduledHours.toFixed(1) : undefined, failed: todayFailed, note: 'Planned hours for today' },
  ];

  return <section aria-labelledby="today-kpis-title">
    <Text id="today-kpis-title" component="h2" fw={650} size="lg" mb="sm">Today</Text>
    <SimpleGrid cols={{ base: 1, sm: 3 }} spacing="sm">
      {tiles.map(tile => <Paper key={tile.label} withBorder radius="md" p={{ base: 'sm', sm: 'md' }} className="office-card">
        <Text size="xs" c="dimmed">{tile.label}</Text>
        <Text fw={700} size="xl" aria-live="polite">{tile.failed ? 'Could not load' : (tile.value ?? '—')}</Text>
        <Text size="xs" c="dimmed">{tile.note}</Text>
      </Paper>)}
    </SimpleGrid>
  </section>;
}
