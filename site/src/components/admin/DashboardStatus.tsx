'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { Button, Group, Paper, SimpleGrid, Text } from '@mantine/core';
import { adminWorkQueue } from '@/lib/adminApi';
import { staffInboxApi } from '@/lib/staffInboxApi';
import { useQuoteRequests } from '@/components/QuoteRequests';

type Counts = { needsAction: number; scheduled: number; inbox: number };

/**
 * What needs attention now, from the same sources as the sidebar and the work queue. Each count links to its list.
 * A failed read shows a retry instead of a zero, so a count is never mistaken for "nothing to do".
 */
export function DashboardStatus() {
  const queue = useQuoteRequests();
  const [counts, setCounts] = useState<Counts | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    setFailed(false);
    const [work, inbox] = await Promise.all([
      adminWorkQueue.page({ view: 'needs-action', source: 'all', skip: 0, take: 1 }),
      staffInboxApi.list().catch(() => null),
    ]);
    if (!work || !inbox) { setFailed(true); return; }
    setCounts({ needsAction: work.counts['needs-action'] ?? 0, scheduled: work.counts.scheduled ?? 0, inbox: inbox.unread });
  }, []);

  useEffect(() => { void load(); }, [load]);

  const partner = queue ? queue.branches.length + queue.requestCount + queue.changeCount : null;
  const tiles = [
    { label: 'Visits needing action', value: counts?.needsAction, href: '/admin/work-queue/', note: 'Unassigned, overdue or not started' },
    { label: 'Scheduled visits', value: counts?.scheduled, href: '/admin/work-queue/', note: 'Accepted or assigned' },
    { label: 'Partner actions', value: partner ?? undefined, href: '/admin/partner-requests/', note: 'Quotes, requests and changes to arrange' },
    { label: 'Unread notifications', value: counts?.inbox, href: '/account/', note: 'Staff alerts in your inbox' },
  ];

  return <section aria-labelledby="dashboard-status-title">
    <Group justify="space-between" mb="sm"><Text id="dashboard-status-title" component="h2" fw={650} size="lg">What needs attention</Text>
      {failed && <Button size="xs" variant="light" onClick={() => void load()}>Retry status</Button>}</Group>
    <SimpleGrid cols={{ base: 2, sm: 4 }} spacing="sm">
      {tiles.map(tile => <Paper key={tile.label} component={Link} href={tile.href} withBorder radius="md" p={{ base: 'sm', sm: 'md' }} className="admin-destination">
        <Text size="xs" c="dimmed">{tile.label}</Text>
        <Text fw={700} size="xl" aria-label={`${tile.label}: ${tile.value ?? (failed ? 'unavailable' : 'loading')}`}>{tile.value ?? (failed ? '–' : '…')}</Text>
        <Text size="xs" c="dimmed" visibleFrom="sm">{tile.note}</Text>
      </Paper>)}
    </SimpleGrid>
  </section>;
}
