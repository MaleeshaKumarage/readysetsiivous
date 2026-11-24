'use client';

import { useEffect, useState } from 'react';
import { Badge, Button, Group, Stack, Table, Text } from '@mantine/core';
import { showError, showSuccess } from '@/lib/toast';
import { supplyRequests, type SupplyDecision, type SupplyRequest, type SupplyStatus } from '@/lib/supplyRequestApi';

const FILTERS: { value: SupplyStatus | 'All'; label: string }[] = [
  { value: 'Requested', label: 'Requested' },
  { value: 'Approved', label: 'Approved' },
  { value: 'Fulfilled', label: 'Fulfilled' },
  { value: 'Declined', label: 'Declined' },
  { value: 'All', label: 'All' },
];

// Admins and managers review employee supply requests. Each decision sends the request's current version.
export function AdminSupplyRequests() {
  const [filter, setFilter] = useState<SupplyStatus | 'All'>('Requested');
  const [items, setItems] = useState<SupplyRequest[] | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  async function load(next = filter) {
    setItems(null);
    try {
      setItems(await supplyRequests.review(next === 'All' ? undefined : next));
    } catch (e) {
      showError(e instanceof Error ? e.message : 'Supply requests could not be loaded.');
      setItems([]);
    }
  }

  useEffect(() => { void load(filter); }, [filter]);

  async function decide(request: SupplyRequest, decision: SupplyDecision) {
    setBusyId(request.id);
    try {
      await supplyRequests.decide(request.id, request.version, decision);
      showSuccess(decision === 'approve' ? 'Request approved.' : decision === 'decline' ? 'Request declined.' : 'Request marked fulfilled.');
      await load(filter);
    } catch (e) {
      showError(e instanceof Error ? e.message : 'The decision could not be saved.');
      await load(filter);
    } finally {
      setBusyId(null);
    }
  }

  return <Stack gap="md">
    <Group gap="xs" role="tablist" aria-label="Supply request status">
      {FILTERS.map(f => <Button key={f.value} role="tab" aria-selected={filter === f.value} variant={filter === f.value ? 'filled' : 'light'} size="xs" onClick={() => setFilter(f.value)}>{f.label}</Button>)}
    </Group>
    {items === null && <Text c="dimmed">Loading…</Text>}
    {items !== null && items.length === 0 && <Text c="dimmed">No supply requests for this status.</Text>}
    {items !== null && items.length > 0 && <Table.ScrollContainer minWidth={720}><Table striped withTableBorder>
      <Table.Thead><Table.Tr><Table.Th>Employee</Table.Th><Table.Th>Items</Table.Th><Table.Th>Note</Table.Th><Table.Th>Status</Table.Th><Table.Th>Actions</Table.Th></Table.Tr></Table.Thead>
      <Table.Tbody>
        {items.map(request => <Table.Tr key={request.id}>
          <Table.Td>{request.employeeName}</Table.Td>
          <Table.Td>{request.lines.map(line => `${line.quantity} ${line.unit} ${line.itemName}`).join(', ')}</Table.Td>
          <Table.Td>{request.note || '—'}</Table.Td>
          <Table.Td><Badge variant="light">{request.status}</Badge></Table.Td>
          <Table.Td>
            <Group gap="xs">
              {request.status === 'Requested' && <>
                <Button size="xs" loading={busyId === request.id} onClick={() => void decide(request, 'approve')}>Approve</Button>
                <Button size="xs" variant="light" color="red" loading={busyId === request.id} onClick={() => void decide(request, 'decline')}>Decline</Button>
              </>}
              {request.status === 'Approved' && <Button size="xs" loading={busyId === request.id} onClick={() => void decide(request, 'fulfil')}>Mark fulfilled</Button>}
            </Group>
          </Table.Td>
        </Table.Tr>)}
      </Table.Tbody>
    </Table></Table.ScrollContainer>}
  </Stack>;
}
