'use client';

import { useEffect, useRef, useState } from 'react';
import { Title, Button, Table, Modal, Group, Stack, Text, Loader, Badge, Select } from '@mantine/core';
import { isAdmin } from '@/lib/auth';
import { adminBookings, type Booking } from '@/lib/adminApi';

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

  // Only the most recent request may update state, so a slow earlier response cannot overwrite a newer filter's rows.
  const requestSeq = useRef(0);
  const load = async (status: string) => {
    const seq = ++requestSeq.current;
    setLoading(true); setLoadError('');
    const query = status ? `?status=${encodeURIComponent(status)}` : '';
    const records = await adminBookings.list(query);
    if (seq !== requestSeq.current) return;
    if (records) setBookings(records); else setLoadError('Bookings could not be loaded. Reload and retry.');
    setLoading(false);
  };

  useEffect(() => { load(statusFilter); }, [statusFilter]);

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

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2} component="h1">Bookings</Title>
        <Select aria-label="Filter by status" data={STATUS_OPTIONS} value={statusFilter} onChange={value => setStatusFilter(value ?? '')} allowDeselect={false} w={200} />
      </Group>

      {message && <Text role="status" c="green" mb="sm">{message}</Text>}
      {loadError && <Text role="alert" c="red" mb="sm">{loadError}</Text>}

      {loading ? <Loader /> : bookings.length === 0 ? (
        <Text c="dimmed">No bookings{statusFilter ? ` with status ${statusFilter}` : ''}.</Text>
      ) : (
        <Table.ScrollContainer minWidth={600}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr><Table.Th>Booking</Table.Th><Table.Th>When</Table.Th><Table.Th>Customer</Table.Th><Table.Th>Service</Table.Th><Table.Th>Status</Table.Th><Table.Th>Employee</Table.Th><Table.Th w={110}></Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {bookings.map(booking => (
                <Table.Tr key={booking.id}>
                  <Table.Td fw={500}>{booking.bookingNumber}</Table.Td>
                  <Table.Td>{booking.startLocalDate} {booking.startLocalTime}</Table.Td>
                  <Table.Td>{booking.customer.name}</Table.Td>
                  <Table.Td>{booking.service.nameFi}</Table.Td>
                  <Table.Td><Badge color={STATUS_COLOR[booking.status] ?? 'gray'}>{booking.status}</Badge></Table.Td>
                  <Table.Td>{booking.employeeName ?? 'Unassigned'}</Table.Td>
                  <Table.Td><Button size="xs" variant="light" onClick={() => { setSelected(booking); setActionError(''); }}>Details</Button></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <Modal opened={selected !== null} onClose={() => { setSelected(null); setActionError(''); }} title={selected ? `Booking ${selected.bookingNumber}` : ''} radius="md">
        {selected && (
          <Stack gap="sm">
            <Text size="sm"><strong>Status:</strong> <span>{selected.status}</span></Text>
            <Text size="sm"><strong>When:</strong> {selected.startLocalDate} {selected.startLocalTime}</Text>
            <Text size="sm"><strong>Customer:</strong> {selected.customer.name} · {selected.customer.phone} · {selected.customer.email}</Text>
            <Text size="sm"><strong>Service:</strong> {selected.service.nameFi} ({selected.service.durationMinutes} min)</Text>
            <Text size="sm"><strong>Employee:</strong> {selected.employeeName ?? 'Unassigned'}</Text>
            {actionError && <Text role="alert" size="sm" c="red">{actionError}</Text>}
            {availableActions(selected.status).length === 0 ? (
              <Text size="sm" c="dimmed">This booking is {selected.status.toLowerCase()}. No further changes are available.</Text>
            ) : isAdmin() && (
              <Group gap="xs" wrap="wrap">
                {availableActions(selected.status).map(action => (
                  <Button key={action} variant={action === 'cancel' ? 'light' : 'filled'} color={action === 'cancel' ? 'red' : undefined} loading={busy === action} disabled={busy !== null} onClick={() => runAction(action)}>
                    {ACTION_LABEL[action]}
                  </Button>
                ))}
              </Group>
            )}
          </Stack>
        )}
      </Modal>
    </>
  );
}
