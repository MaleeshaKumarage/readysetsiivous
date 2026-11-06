'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { Title, Button, Group, Stack, Text, Loader, Badge, Tabs, Select, TextInput, Drawer, Paper } from '@mantine/core';
import {
  adminWorkQueue, adminBookings, adminEmployees, type Booking, type Employee, type WorkQueueItem, type WorkQueuePage, type WorkQueueSource, type WorkQueueView,
} from '@/lib/adminApi';

const VIEWS: { value: WorkQueueView; label: string }[] = [
  { value: 'needs-action', label: 'Needs action' },
  { value: 'scheduled', label: 'Scheduled' },
  { value: 'in-progress', label: 'In progress' },
  { value: 'completed', label: 'Completed' },
  { value: 'cancelled', label: 'Cancelled' },
];
const SOURCES: { value: WorkQueueSource; label: string }[] = [
  { value: 'all', label: 'All sources' },
  { value: 'booking', label: 'Public bookings' },
  { value: 'partner', label: 'Partner requests' },
  { value: 'shift', label: 'Company shifts' },
];
const PAGE_SIZE = 25;
const VIEW_VALUES = VIEWS.map(view => view.value);
const SOURCE_VALUES = SOURCES.map(source => source.value);

/** Booking actions allowed per stored status. The server enforces the same transitions. */
function bookingActions(status: string): ('confirm' | 'cancel' | 'complete')[] {
  if (status === 'New') return ['confirm', 'cancel'];
  if (status === 'Confirmed') return ['complete', 'cancel'];
  return [];
}

const ACTION_LABEL = { confirm: 'Confirm', cancel: 'Cancel booking', complete: 'Mark completed' } as const;

const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

/** A yyyy-MM-dd string that is a real calendar date (rejects 2026-13-45 and 2026-02-30). */
function isCalendarDate(value: string): boolean {
  if (!DATE_PATTERN.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

/** The first view shows this many days from today (tenant time zone), so a busy schedule opens on work that is due soon. */
const DEFAULT_WINDOW_DAYS = 14;
/** Assignee filter value for work with no cleaner. Sent to the server as unassigned=true. */
const UNASSIGNED = 'unassigned';
function defaultWindow(): { dateFrom: string; dateTo: string } {
  const today = new Date().toLocaleDateString('en-CA', { timeZone: 'Europe/Helsinki' });
  const end = new Date(Date.parse(`${today}T12:00:00Z`) + (DEFAULT_WINDOW_DAYS - 1) * 86_400_000).toISOString().slice(0, 10);
  return { dateFrom: today, dateTo: end };
}
/** Accepts real yyyy-MM-dd bounds only; a reversed range is dropped rather than sent to the server. */
function readDates(from: string | null, to: string | null): { dateFrom: string; dateTo: string } {
  const start = from && isCalendarDate(from) ? from : '';
  const end = to && isCalendarDate(to) ? to : '';
  return start && end && start > end ? { dateFrom: '', dateTo: '' } : { dateFrom: start, dateTo: end };
}

export default function WorkQueuePage() {
  const [view, setView] = useState<WorkQueueView>('needs-action');
  const [source, setSource] = useState<WorkQueueSource>('all');
  const [query, setQuery] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [assignee, setAssignee] = useState('');
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [result, setResult] = useState<WorkQueuePage | null>(null);
  const [loading, setLoading] = useState(true);
  const [listError, setListError] = useState('');
  const [reload, setReload] = useState(0);
  const [ready, setReady] = useState(false);
  const generation = useRef(0);

  const [openBooking, setOpenBooking] = useState<Booking | null>(null);
  const [detailError, setDetailError] = useState('');
  const [actionBusy, setActionBusy] = useState<string | null>(null);

  type Filters = { view: WorkQueueView; source: WorkQueueSource; search: string; page: number; dateFrom: string; dateTo: string; assignee: string };
  const current = (overrides: Partial<Filters> = {}): Filters => ({ view, source, search, page, dateFrom, dateTo, assignee, ...overrides });

  const writeUrl = (next: Filters, mode: 'push' | 'replace' = 'push') => {
    try {
      const params = new URLSearchParams(window.location.search);
      params.set('view', next.view);
      params.set('source', next.source);
      if (next.search) params.set('q', next.search); else params.delete('q');
      if (next.dateFrom) params.set('from', next.dateFrom); else params.delete('from');
      if (next.dateTo) params.set('to', next.dateTo); else params.delete('to');
      if (!next.dateFrom && !next.dateTo) params.set('range', 'all'); else params.delete('range');
      // An explicit 'all' keeps a cleared assignee from falling back to the default on reload.
      params.set('assignee', next.assignee || 'all');
      params.set('page', String(next.page));
      const qs = params.toString();
      const url = `${window.location.pathname}?${qs}`;
      if (mode === 'push') window.history.pushState(null, '', url); else window.history.replaceState(null, '', url);
    } catch { /* URL state is a convenience; the queue still works without it */ }
  };

  const readUrl = () => {
    const params = new URLSearchParams(window.location.search);
    const requestedView = params.get('view') as WorkQueueView | null;
    const requestedSource = params.get('source') as WorkQueueSource | null;
    const requestedPage = Number(params.get('page'));
    const view = requestedView && VIEW_VALUES.includes(requestedView) ? requestedView : 'needs-action' as WorkQueueView;
    const chosenQueue = params.has('from') || params.has('to') || params.has('range') || params.has('assignee');
    const dates = chosenQueue ? readDates(params.get('from'), params.get('to'))
      : view === 'needs-action' ? defaultWindow() : { dateFrom: '', dateTo: '' };
    const rawAssignee = (params.get('assignee') ?? '').trim();
    // A fresh Needs-action link opens on unassigned work. 'all' means Anyone, and a named employee is kept as chosen.
    const assignee = chosenQueue ? (rawAssignee === 'all' ? '' : rawAssignee) : view === 'needs-action' ? UNASSIGNED : '';
    return {
      view,
      source: requestedSource && SOURCE_VALUES.includes(requestedSource) ? requestedSource : 'all' as WorkQueueSource,
      search: (params.get('q') ?? '').trim(),
      page: Number.isInteger(requestedPage) && requestedPage >= 1 ? requestedPage : 1,
      ...dates,
      assignee,
      booking: params.get('booking'),
    };
  };

  // Restore state from the URL after mount, and on browser Back/Forward.
  useEffect(() => {
    const apply = () => {
      const state = readUrl();
      setView(state.view); setSource(state.source); setQuery(state.search); setSearch(state.search); setPage(state.page);
      setDateFrom(state.dateFrom); setDateTo(state.dateTo); setAssignee(state.assignee);
    };
    apply();
    setReady(true);
    window.addEventListener('popstate', apply);
    return () => { window.removeEventListener('popstate', apply); generation.current++; };
  }, []);

  // Open a deep-linked booking once.
  const openedBooking = useRef<string | null>(null);
  useEffect(() => {
    if (!ready) return;
    const requested = readUrl().booking;
    if (!requested || openedBooking.current === requested) return;
    openedBooking.current = requested;
    void openDetail(requested);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready]);

  // Debounce free-text search into the applied search.
  useEffect(() => {
    if (!ready) return;
    const timer = window.setTimeout(() => {
      const next = query.trim();
      if (next === search) return;
      setSearch(next); setPage(1);
      writeUrl(current({ search: next, page: 1 }));
    }, 300);
    return () => window.clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query]);

  // Load the current page. Stale responses are ignored by generation.
  useEffect(() => {
    if (!ready) return;
    const version = ++generation.current;
    setLoading(true); setListError('');
    void adminWorkQueue.page({ view, source, q: search || undefined, from: dateFrom || undefined, to: dateTo || undefined, assignee: assignee || undefined, skip: (page - 1) * PAGE_SIZE, take: PAGE_SIZE }).then(next => {
      if (version !== generation.current) return;
      if (!next) { setListError('The work queue could not be loaded. Retry.'); setResult(null); }
      else if (next.items.length === 0 && next.total > 0 && page > 1) {
        const last = Math.ceil(next.total / PAGE_SIZE);
        setPage(last); writeUrl(current({ page: last }), 'replace');
        return;
      } else setResult(next);
      setLoading(false);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready, view, source, search, page, dateFrom, dateTo, assignee, reload]);

  // Employees feed the assignee filter. A failure leaves the filter on "Anyone" rather than blocking the queue.
  useEffect(() => {
    if (!ready) return;
    void adminEmployees.list(false).then(list => setEmployees(list ?? [])).catch(() => setEmployees([]));
  }, [ready]);

  const changeView = (next: WorkQueueView) => {
    setView(next); setPage(1); writeUrl(current({ view: next, page: 1 }));
  };
  const changeSource = (next: WorkQueueSource) => {
    setSource(next); setPage(1); writeUrl(current({ source: next, page: 1 }));
  };
  const windowDates = defaultWindow();
  const defaultWindowActive = dateFrom === windowDates.dateFrom && dateTo === windowDates.dateTo;
  // Show everything: clears the dates and the assignee together, so one URL change carries all three.
  const showEverything = () => { setDateFrom(''); setDateTo(''); setAssignee(''); setPage(1); writeUrl(current({ dateFrom: '', dateTo: '', assignee: '', page: 1 })); };
  const changeDates = (nextFrom: string, nextTo: string) => {
    // Input edits go through the same rule as the URL, so a reversed or invalid pair is cleared before it is stored or sent.
    const next = readDates(nextFrom, nextTo);
    setDateFrom(next.dateFrom); setDateTo(next.dateTo); setPage(1); writeUrl(current({ dateFrom: next.dateFrom, dateTo: next.dateTo, page: 1 }));
  };
  const changeAssignee = (next: string) => {
    setAssignee(next); setPage(1); writeUrl(current({ assignee: next, page: 1 }));
  };
  const changePage = (next: number) => {
    setPage(next); writeUrl(current({ page: next }));
  };

  const openDetail = async (id: string) => {
    setDetailError('');
    const booking = await adminBookings.detail(id);
    if (!booking) { setDetailError('This booking could not be loaded. Retry.'); }
    setOpenBooking(booking ?? null);
  };

  const runAction = async (booking: Booking, action: 'confirm' | 'cancel' | 'complete') => {
    if (action === 'cancel' && !window.confirm(`Cancel booking ${booking.bookingNumber}?`)) return;
    setActionBusy(action); setDetailError('');
    const ok = action === 'confirm' ? await adminBookings.confirm(booking.id)
      : action === 'cancel' ? await adminBookings.cancel(booking.id)
      : await adminBookings.complete(booking.id);
    setActionBusy(null);
    if (!ok) { setDetailError('The action could not be completed. Refresh and check the booking status.'); return; }
    const refreshed = await adminBookings.detail(booking.id);
    setOpenBooking(refreshed ?? null);
    setReload(n => n + 1);
  };

  const onOpen = (item: WorkQueueItem) => {
    if (item.source === 'booking') void openDetail(item.id);
  };

  const pageCount = result ? Math.max(1, Math.ceil(result.total / PAGE_SIZE)) : 1;
  const from = result && result.total > 0 ? (page - 1) * PAGE_SIZE + 1 : 0;
  const to = result ? Math.min(result.total, page * PAGE_SIZE) : 0;

  return (
    <>
      <Title order={2} component="h1" mb="md">Work queue</Title>

      <Tabs value={view} onChange={value => value && changeView(value as WorkQueueView)}>
        <Tabs.List aria-label="Work queue views" className="admin-tabs-list">
          {VIEWS.map(item => (
            <Tabs.Tab key={item.value} value={item.value}>
              {item.label}{result ? ` (${result.counts[item.value]})` : ''}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>

      <Group mt="md" mb="md" align="flex-end" wrap="wrap" gap="sm">
        <Select aria-label="Source" label="Source" data={SOURCES} value={source}
          onChange={value => value && changeSource(value as WorkQueueSource)} allowDeselect={false} w={220} />
        <TextInput label="Search" placeholder="Customer, booking number or company" value={query}
          onChange={event => setQuery(event.currentTarget.value)} w={320} />
        <TextInput type="date" label="From" aria-label="From date" value={dateFrom} max={dateTo || undefined}
          onChange={event => changeDates(event.currentTarget.value, dateTo)} w={170} />
        <TextInput type="date" label="To" aria-label="To date" value={dateTo} min={dateFrom || undefined}
          onChange={event => changeDates(dateFrom, event.currentTarget.value)} w={170} />
        <Select aria-label="Assignee" label="Assignee" placeholder="Anyone" clearable clearButtonProps={{ 'aria-label': 'Clear assignee' }}
          data={[{ value: UNASSIGNED, label: 'Unassigned' }, ...employees.map(employee => ({ value: employee.id, label: `${employee.firstName} ${employee.lastName}`.trim() }))]}
          value={assignee || null} onChange={value => changeAssignee(value ?? '')} w={220} />
      </Group>
      {defaultWindowActive && (
        <Group gap="xs" mb="md" role="status">
          <Text size="sm">{assignee === UNASSIGNED ? 'Showing unassigned work' : 'Showing'} for the next {DEFAULT_WINDOW_DAYS} days ({dateFrom} to {dateTo}).{assignee === UNASSIGNED ? ' Assigned work is hidden.' : ''}</Text>
          <Button size="xs" variant="subtle" onClick={showEverything}>Show all work</Button>
        </Group>
      )}

      {assignee && assignee !== UNASSIGNED && <Text size="sm" c="dimmed" mb="sm">Partner requests have no assignee, so they are hidden while this filter is on.</Text>}

      {loading ? <Loader /> : listError ? (
        <Stack>
          <Text role="alert" c="red">{listError}</Text>
          <Button variant="light" onClick={() => setReload(n => n + 1)}>Retry</Button>
        </Stack>
      ) : !result || result.items.length === 0 ? (
        <Text c="dimmed">{search ? `Nothing in this view matches “${search}”.` : 'Nothing in this view.'}</Text>
      ) : (
        <Stack gap="xs" role="list" aria-label="Work items">
          {result.items.map(item => (
            <Paper key={`${item.source}-${item.id}`} withBorder p="sm" role="listitem" data-state={item.stateLabel === 'Not started' ? 'not-started' : undefined} style={item.stateLabel === 'Not started' ? { borderColor: 'var(--mantine-color-red-6)' } : undefined}>
              <Group justify="space-between" wrap="wrap" gap="xs">
                <Stack gap={2}>
                  <Text fw={600}>{item.customerName}</Text>
                  <Text size="sm" c="dimmed">{item.reference} · {item.scheduledLocal}</Text>
                  <Group gap={6}>
                    <Badge size="sm" variant="light" color={item.stateLabel === 'Not started' ? 'red' : undefined}>{item.stateLabel}</Badge>
                    <Badge size="sm" variant="outline" color="gray">{item.source === 'booking' ? 'Public booking' : item.source === 'shift' ? 'Company shift' : 'Partner request'}</Badge>
                    {item.assigneeName && <Text size="xs">{item.assigneeName}</Text>}
                  </Group>
                </Stack>
                {item.source === 'booking' ? (
                  <Button size="sm" variant="light" onClick={() => onOpen(item)} aria-label={`Open booking ${item.reference}`}>Open</Button>
                ) : (
                  <Button size="sm" variant="light" component={Link} href={item.href} aria-label={item.source === 'shift' ? `Open company shift ${item.reference}` : `Open partner request for ${item.customerName}`}>Open</Button>
                )}
              </Group>
            </Paper>
          ))}
        </Stack>
      )}

      {result && result.total > 0 && (
        <Group justify="space-between" mt="md" wrap="wrap" gap="sm">
          <Text size="sm">Showing {from}–{to} of {result.total}</Text>
          <Group gap="xs">
            <Button variant="default" size="sm" disabled={page <= 1 || loading} onClick={() => changePage(page - 1)}>Previous</Button>
            <Text size="sm">Page {page} of {pageCount}</Text>
            <Button variant="default" size="sm" disabled={page >= pageCount || loading} onClick={() => changePage(page + 1)}>Next</Button>
          </Group>
        </Group>
      )}

      <Drawer opened={openBooking !== null || detailError !== ''} onClose={() => { setOpenBooking(null); setDetailError(''); }}
        title={openBooking ? `Booking ${openBooking.bookingNumber}` : 'Booking'} position="right" size="md">
        {detailError && <Text role="alert" c="red" mb="sm">{detailError}</Text>}
        {openBooking && (
          <Stack gap="sm">
            <Text><strong>Customer:</strong> {openBooking.customer.name}</Text>
            <Text><strong>Contact:</strong> {openBooking.customer.phone} · {openBooking.customer.email}</Text>
            <Text><strong>Service:</strong> {openBooking.service.nameFi}, {openBooking.service.durationMinutes} min</Text>
            <Text><strong>Start:</strong> {openBooking.startLocalDate} {openBooking.startLocalTime}</Text>
            <Text><strong>Status:</strong> {openBooking.status}</Text>
            <Text><strong>Assigned to:</strong> {openBooking.employeeName ?? 'Unassigned'}</Text>
            <Group gap="xs" wrap="wrap">
              {bookingActions(openBooking.status).map(action => (
                <Button key={action} size="sm" variant={action === 'cancel' ? 'light' : 'filled'} color={action === 'cancel' ? 'red' : undefined}
                  loading={actionBusy === action} disabled={actionBusy !== null} onClick={() => void runAction(openBooking, action)}>
                  {ACTION_LABEL[action]}
                </Button>
              ))}
            </Group>
          </Stack>
        )}
      </Drawer>
    </>
  );
}
