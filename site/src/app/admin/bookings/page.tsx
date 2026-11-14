'use client';

import { DsGrid } from '@/components/ds/DsGrid';
import { useEffect, useRef, useState } from 'react';
import { Title, Button, Table, Modal, Group, Stack, Text, Loader, Badge, Select, SegmentedControl, TextInput } from '@mantine/core';
import { isAdmin } from '@/lib/auth';
import { adminBookings, adminEmployees, type Booking } from '@/lib/adminApi';

const STATUS_OPTIONS = [
  { value: '', label: 'All statuses' },
  { value: 'New', label: 'New' },
  { value: 'Confirmed', label: 'Confirmed' },
  { value: 'Completed', label: 'Completed' },
  { value: 'Cancelled', label: 'Cancelled' },
];

type BookingAction = 'confirm' | 'cancel' | 'complete';

// Terminal states offer no actions. The API would accept some of these transitions (e.g. cancelling a completed booking), so the UI must not offer them.
function availableActions(status: string): BookingAction[] {
  if (status === 'New') return ['confirm', 'cancel'];
  if (status === 'Confirmed') return ['complete', 'cancel'];
  return [];
}

// Assignment is only offered while a booking is still open; terminal bookings keep the no-changes message.
function canAssign(status: string) {
  return status === 'New' || status === 'Confirmed';
}

const LOAD_FAILED = 'Bookings could not be loaded. Reload and retry.';
const DAY_LOAD_FAILED = 'The day schedule could not be loaded. Reload and retry.';
const EMPLOYEES_LOAD_FAILED = 'Employees could not be loaded, so bookings cannot be assigned. Reload and retry.';

const ACTION_LABEL: Record<BookingAction, string> = { confirm: 'Confirm', cancel: 'Cancel booking', complete: 'Mark completed' };
const STATUS_COLOR: Record<string, string> = { New: 'blue', Confirmed: 'green', Completed: 'gray', Cancelled: 'red' };

export default function BookingsAdminPage() {
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [statusFilter, setStatusFilter] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [selected, setSelected] = useState<Booking | null>(null);
  const [busy, setBusy] = useState<BookingAction | null>(null);
  const [actionError, setActionError] = useState('');
  const [message, setMessage] = useState('');
  const [employees, setEmployees] = useState<{ id: string; name: string; isActive: boolean }[]>([]);
  const [historyError, setHistoryError] = useState(false);
  const [assigneeId, setAssigneeId] = useState<string | null>(null);
  const [assignBusy, setAssignBusy] = useState(false);
  const [employeesError, setEmployeesError] = useState('');
  const [view, setView] = useState<'bookings' | 'day'>('bookings');
  const [dayDate, setDayDate] = useState('');
  const [dayEmployeeId, setDayEmployeeId] = useState<string | null>(null);
  const [dayRows, setDayRows] = useState<Booking[]>([]);
  const [dayLoading, setDayLoading] = useState(false);
  const [dayError, setDayError] = useState('');
  const dayRequestSeq = useRef(0);

  // Only the most recent request may update state, so a slow earlier response cannot overwrite a newer filter's rows.
  const requestSeq = useRef(0);
  const load = async (status: string) => {
    const seq = ++requestSeq.current;
    setLoading(true); setLoadError('');
    try {
      const query = status ? `?status=${encodeURIComponent(status)}` : '';
      const records = await adminBookings.list(query);
      if (seq !== requestSeq.current) return;
      if (records) setBookings(records);
      else { setBookings([]); setLoadError(LOAD_FAILED); }
    } catch {
      if (seq !== requestSeq.current) return;
      setBookings([]); setLoadError(LOAD_FAILED);
    } finally {
      if (seq === requestSeq.current) setLoading(false);
    }
  };

  useEffect(() => { load(statusFilter); }, [statusFilter]);

  useEffect(() => {
    // Includes inactive employees so the day view can show a past schedule for someone since deactivated; the assign picker filters them out.
    adminEmployees.list(true)
      .then(records => {
        if (records) setEmployees(records.map(e => ({ id: e.id, name: `${e.firstName} ${e.lastName}`, isActive: e.isActive })));
        else setEmployeesError(EMPLOYEES_LOAD_FAILED);
      })
      .catch(() => setEmployeesError(EMPLOYEES_LOAD_FAILED));
  }, []);

  // The list omits history, so the detail panel loads it once a booking is opened.
  useEffect(() => {
    if (!selected) return;
    let active = true;
    setHistoryError(false);
    adminBookings.detail(selected.id).then(detail => {
      if (!active) return;
      if (detail) setSelected(current => (current && current.id === detail.id ? { ...current, history: detail.history } : current));
      else setHistoryError(true);
    }).catch(() => { if (active) setHistoryError(true); });
    return () => { active = false; };
  }, [selected?.id]);

  // The day view only queries once both a day and an employee are chosen; stale responses are ignored.
  useEffect(() => {
    if (view !== 'day' || !dayDate || !dayEmployeeId) return;
    const seq = ++dayRequestSeq.current;
    setDayLoading(true); setDayError('');
    adminBookings.schedule(dayDate, dayEmployeeId)
      .then(records => {
        if (seq !== dayRequestSeq.current) return;
        if (records) setDayRows(records); else { setDayRows([]); setDayError(DAY_LOAD_FAILED); }
      })
      .catch(() => { if (seq === dayRequestSeq.current) { setDayRows([]); setDayError(DAY_LOAD_FAILED); } })
      .finally(() => { if (seq === dayRequestSeq.current) setDayLoading(false); });
  }, [view, dayDate, dayEmployeeId]);

  // Changing the day or employee clears the previous results in the same render, so stale rows or messages never show for the new selection.
  const changeDaySelection = (date: string, employeeId: string | null) => {
    dayRequestSeq.current++; // discard any in-flight response for the previous selection
    setDayDate(date); setDayEmployeeId(employeeId);
    setDayRows([]); setDayError('');
    setDayLoading(Boolean(date && employeeId));
  };

  const runAction = async (action: BookingAction) => {
    if (!selected) return;
    setBusy(action); setActionError(''); setMessage('');
    try {
      const ok = action === 'confirm' ? await adminBookings.confirm(selected.id)
        : action === 'cancel' ? await adminBookings.cancel(selected.id)
        : await adminBookings.complete(selected.id);
      if (!ok) { setActionError(`Could not ${ACTION_LABEL[action].toLowerCase()}. The booking may have changed; reload and retry.`); return; }
      const nextStatus = action === 'confirm' ? 'Confirmed' : action === 'cancel' ? 'Cancelled' : 'Completed';
      setMessage(`${selected.bookingNumber} is now ${nextStatus}.`);
      setSelected(null);
      await load(statusFilter);
    } catch {
      setActionError('Could not update the booking. Please retry.');
    } finally { setBusy(null); }
  };

  const assign = async () => {
    if (!selected || !assigneeId) return;
    setAssignBusy(true); setActionError(''); setMessage('');
    try {
      const ok = await adminBookings.assign(selected.id, assigneeId);
      if (!ok) { setActionError('Could not assign the booking. Reload and retry.'); return; }
      const name = employees.find(e => e.id === assigneeId)?.name ?? 'the employee';
      setMessage(`${selected.bookingNumber} assigned to ${name}.`);
      setSelected(null); setAssigneeId(null);
      await load(statusFilter);
    } catch {
      setActionError('Could not assign the booking. Please retry.');
    } finally { setAssignBusy(false); }
  };

  const unassign = async () => {
    if (!selected) return;
    setAssignBusy(true); setActionError(''); setMessage('');
    try {
      const ok = await adminBookings.unassign(selected.id);
      if (!ok) { setActionError('Could not unassign the booking. Reload and retry.'); return; }
      setMessage(`${selected.bookingNumber} is unassigned.`);
      setSelected(null); setAssigneeId(null);
      await load(statusFilter);
    } catch {
      setActionError('Could not unassign the booking. Please retry.');
    } finally { setAssignBusy(false); }
  };

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2} component="h1">Bookings</Title>
        <Select aria-label="Filter by status" data={STATUS_OPTIONS} value={statusFilter} onChange={value => setStatusFilter(value ?? '')} allowDeselect={false} w={200} />
      </Group>

      <SegmentedControl mb="md" value={view} onChange={value => setView(value as 'bookings' | 'day')} data={[{ value: 'bookings', label: 'Bookings' }, { value: 'day', label: 'Day view' }]} />

      {message && <Text role="status" c="green" mb="sm">{message}</Text>}
      {view === 'day' ? (
        <>
          {employeesError && <Text role="alert" c="red" mb="sm">{employeesError}</Text>}
          <Group align="flex-end" mb="md" wrap="wrap">
            <TextInput type="date" label="Day" value={dayDate} onChange={e => changeDaySelection(e.currentTarget.value, dayEmployeeId)} w={200} />
            <Select label="Employee" searchable disabled={Boolean(employeesError)} placeholder="Choose employee" data={employees.map(e => ({ value: e.id, label: e.name }))} value={dayEmployeeId} onChange={value => changeDaySelection(dayDate, value)} w={240} />
          </Group>
          {!dayDate || !dayEmployeeId ? (
            <Text c="dimmed">Choose a day and an employee to see their bookings.</Text>
          ) : dayLoading ? <Loader /> : dayError ? (
            <Text role="alert" c="red">{dayError}</Text>
          ) : dayRows.length === 0 ? (
            <Text c="dimmed">No bookings for this employee on {dayDate}.</Text>
          ) : (
            <DsGrid<(typeof dayRows)[number]>
                label="Bookings for the day"
                rows={dayRows}
                getRowId={(row) => row.id}
                columns={[
                  { headerName: 'Time', colId: 'time', valueGetter: p => p.data?.startLocalTime ?? '' },
                  { headerName: 'Booking', colId: 'booking', valueGetter: p => p.data?.bookingNumber ?? '' },
                  { headerName: 'Customer', colId: 'customer', flex: 2, valueGetter: p => p.data?.customer.name ?? '' },
                  { headerName: 'Service', colId: 'service', flex: 2, valueGetter: p => p.data?.service.nameFi ?? '' },
                  { headerName: 'Status', colId: 'status', sortable: false, cellRenderer: (p: { data?: (typeof dayRows)[number] }) => p.data ? <Badge color={STATUS_COLOR[p.data.status] ?? 'gray'}>{p.data.status}</Badge> : null },
                ]}
              />
          )}
        </>
      ) : null}

      {view === 'bookings' && (<>
      {loadError && <Text role="alert" c="red" mb="sm">{loadError}</Text>}

      {loading ? <Loader /> : loadError ? null : bookings.length === 0 ? (
        <Text c="dimmed">No bookings{statusFilter ? ` with status ${statusFilter}` : ''}.</Text>
      ) : (
        <DsGrid<(typeof bookings)[number]>
          label="Bookings"
          rows={bookings}
          getRowId={(booking) => booking.id}
          columns={[
            { headerName: 'Booking', colId: 'booking', valueGetter: p => p.data?.bookingNumber ?? '' },
            { headerName: 'When', colId: 'when', valueGetter: p => p.data ? `${p.data.startLocalDate} ${p.data.startLocalTime}` : '' },
            { headerName: 'Customer', colId: 'customer', flex: 2, valueGetter: p => p.data?.customer.name ?? '' },
            { headerName: 'Service', colId: 'service', flex: 2, valueGetter: p => p.data?.service.nameFi ?? '' },
            { headerName: 'Status', colId: 'status', sortable: false, cellRenderer: (p: { data?: (typeof bookings)[number] }) => p.data ? <Badge color={STATUS_COLOR[p.data.status] ?? 'gray'}>{p.data.status}</Badge> : null },
            { headerName: 'Employee', colId: 'employee', valueGetter: p => p.data ? (p.data.employeeName ?? 'Unassigned') : '' },
            { headerName: '', colId: 'details', sortable: false, width: 130, flex: 0, cellRenderer: (p: { data?: (typeof bookings)[number] }) => p.data ? <Button size="xs" variant="light" onClick={() => { setSelected(p.data!); setActionError(''); setAssigneeId(null); }}>Details</Button> : null },
          ]}
        />
      )}
      </>)}

      <Modal opened={selected !== null} onClose={() => { setSelected(null); setActionError(''); }} title={selected ? `Booking ${selected.bookingNumber}` : ''} radius="md">
        {selected && (
          <Stack gap="sm">
            <Text size="sm"><strong>Status:</strong> <span>{selected.status}</span></Text>
            <Text size="sm"><strong>When:</strong> {selected.startLocalDate} {selected.startLocalTime}</Text>
            <Text size="sm"><strong>Customer:</strong> {selected.customer.name} · {selected.customer.phone} · {selected.customer.email}</Text>
            <Text size="sm"><strong>Service:</strong> {selected.service.nameFi} ({selected.service.durationMinutes} min)</Text>
            <Text size="sm"><strong>Employee:</strong> {selected.employeeName ?? 'Unassigned'}</Text>
            {selected.history && selected.history.length > 0 && (
              <Stack gap={4}>
                <Text size="sm" fw={500}>History</Text>
                {selected.history.map((entry, index) => (
                  <Text key={index} size="xs" c="dimmed">{entry.atUtc.replace('T', ' ').slice(0, 16)} UTC · {entry.status} · {entry.by}{entry.note ? ` · ${entry.note}` : ''}</Text>
                ))}
              </Stack>
            )}
            {historyError && <Text role="alert" size="xs" c="red">History could not be loaded.</Text>}
            {actionError && <Text role="alert" size="sm" c="red">{actionError}</Text>}
            {availableActions(selected.status).length === 0 && !canAssign(selected.status) ? (
              <Text size="sm" c="dimmed">This booking is {selected.status.toLowerCase()}. No further changes are available.</Text>
            ) : isAdmin() && availableActions(selected.status).length > 0 && (
              <Group gap="xs" wrap="wrap">
                {availableActions(selected.status).map(action => (
                  <Button key={action} variant={action === 'cancel' ? 'light' : 'filled'} color={action === 'cancel' ? 'red' : undefined} loading={busy === action} disabled={busy !== null} onClick={() => runAction(action)}>
                    {ACTION_LABEL[action]}
                  </Button>
                ))}
              </Group>
            )}
            {isAdmin() && canAssign(selected.status) && (
              <Stack gap="xs">
                {employeesError && <Text role="alert" size="sm" c="red">{employeesError}</Text>}
                <Group gap="xs" wrap="wrap" align="flex-end">
                <Select aria-label="Assign to" placeholder="Choose employee" searchable data={employees.filter(e => e.isActive).map(e => ({ value: e.id, label: e.name }))} value={assigneeId} onChange={setAssigneeId} w={240} />
                <Button variant="light" loading={assignBusy} disabled={!assigneeId || assignBusy} onClick={assign}>Assign</Button>
                {selected.employeeName && <Button variant="subtle" color="red" loading={assignBusy} disabled={assignBusy} onClick={unassign}>Unassign</Button>}
                </Group>
              </Stack>
            )}
          </Stack>
        )}
      </Modal>
    </>
  );
}
