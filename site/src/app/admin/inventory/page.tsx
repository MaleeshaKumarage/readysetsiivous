'use client';

import { useEffect, useRef, useState } from 'react';
import { Title, Tabs, Table, Button, Modal, TextInput, Select, Group, Stack, Text, Loader, Badge, Switch } from '@mantine/core';
import { Download } from 'lucide-react';
import { showError, showSuccess } from '@/lib/toast';
import { adminEmployees, type Employee } from '@/lib/adminApi';
import {
  adminInventory,
  inventoryPhotoUrl,
  takenLabel,
  type InventoryItem,
  type InventoryLocation,
  type InventoryUsage,
} from '@/lib/inventoryApi';

const PAGE_SIZE = 100;

type Filters = { employeeId: string; from: string; to: string };
const NO_FILTERS: Filters = { employeeId: '', from: '', to: '' };

function toFilterParams(filters: Filters) {
  return {
    employeeId: filters.employeeId || undefined,
    from: filters.from || undefined,
    to: filters.to || undefined,
  };
}

function employeeName(employee: Employee) {
  return `${employee.firstName} ${employee.lastName}`.trim() || employee.email;
}

function LogTab() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [draft, setDraft] = useState<Filters>(NO_FILTERS);
  const [applied, setApplied] = useState<Filters>(NO_FILTERS);
  const [rows, setRows] = useState<InventoryUsage[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [hasMore, setHasMore] = useState(false);
  const [photoView, setPhotoView] = useState<InventoryUsage | null>(null);

  useEffect(() => {
    // Include inactive employees so their past entries can still be filtered.
    adminEmployees.list(true).then(list => setEmployees(list ?? [])).catch(() => setEmployees([]));
  }, []);

  useEffect(() => { load(applied, 0); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [applied]);

  // Each request gets a sequence number. Only the latest request may update the table, so a slow response
  // for an older filter cannot overwrite the results of a newer one.
  const latestRequest = useRef(0);

  async function load(filters: Filters, skip: number) {
    const request = ++latestRequest.current;
    setLoading(true);
    try {
      const page = await adminInventory.usage({ ...toFilterParams(filters), skip, take: PAGE_SIZE });
      if (request !== latestRequest.current) return;
      setRows(prev => (skip === 0 || !prev ? page : [...prev, ...page]));
      setHasMore(page.length === PAGE_SIZE);
    } catch (err) {
      if (request === latestRequest.current) showError(err instanceof Error ? err.message : 'Could not load the inventory log.');
    } finally {
      if (request === latestRequest.current) setLoading(false);
    }
  }

  function apply() {
    if (draft.from && draft.to && draft.to < draft.from) {
      showError('The end date must be on or after the start date.');
      return;
    }
    setRows(null);
    // A fresh object, so applying the same filters again still changes state and reloads the log.
    setApplied({ ...draft });
  }

  async function exportCsv() {
    try {
      const blob = await adminInventory.exportCsv(toFilterParams(applied));
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `inventory-${new Date().toISOString().slice(0, 10)}.csv`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      showError(err instanceof Error ? err.message : 'The export could not be created.');
    }
  }

  return (
    <Stack>
      <Group align="flex-end" wrap="wrap">
        <Select
          label="Employee"
          aria-label="Employee"
          placeholder="All employees"
          clearable
          searchable
          data={employees.map(e => ({ value: e.id, label: e.isActive ? employeeName(e) : `${employeeName(e)} (inactive)` }))}
          value={draft.employeeId || null}
          onChange={value => setDraft({ ...draft, employeeId: value ?? '' })}
          style={{ minWidth: 220 }}
        />
        <TextInput type="date" label="From" aria-label="From" value={draft.from} onChange={e => setDraft({ ...draft, from: e.currentTarget.value })} />
        <TextInput type="date" label="To" aria-label="To" value={draft.to} onChange={e => setDraft({ ...draft, to: e.currentTarget.value })} />
        <Button onClick={apply}>Apply</Button>
        <Button variant="light" leftSection={<Download size={16} aria-hidden="true" />} onClick={exportCsv}>Export CSV</Button>
      </Group>

      {rows === null && loading && <Loader size="sm" />}
      {rows !== null && rows.length === 0 && <Text c="dimmed">No entries match these filters.</Text>}
      {rows !== null && rows.length > 0 && (
        <Table.ScrollContainer minWidth={720}>
          <Table striped withTableBorder>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Taken</Table.Th>
                <Table.Th>Employee</Table.Th>
                <Table.Th>Location</Table.Th>
                <Table.Th>Item</Table.Th>
                <Table.Th>Quantity</Table.Th>
                <Table.Th>Note</Table.Th>
                <Table.Th>Photos</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map(row => (
                <Table.Tr key={row.id}>
                  <Table.Td>{takenLabel(row.takenUtc)}</Table.Td>
                  <Table.Td>{row.employeeName}</Table.Td>
                  <Table.Td>{row.locationName}</Table.Td>
                  <Table.Td>{row.itemName}</Table.Td>
                  <Table.Td>{row.quantity} {row.unit}</Table.Td>
                  <Table.Td>{row.note}</Table.Td>
                  <Table.Td>
                    {row.photosPurged ? <Text size="sm" c="dimmed">Deleted</Text> : (
                      <Button size="xs" variant="subtle" onClick={() => setPhotoView(row)}>View {row.photoIds.length}</Button>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      {hasMore && (
        <Group>
          <Button variant="light" loading={loading} onClick={() => load(applied, rows?.length ?? 0)}>Load more</Button>
        </Group>
      )}

      <Modal opened={photoView !== null} onClose={() => setPhotoView(null)} title={photoView ? `${photoView.itemName}, ${photoView.employeeName}` : ''} size="lg">
        {photoView && <PhotoGallery entry={photoView} />}
      </Modal>
    </Stack>
  );
}

function PhotoGallery({ entry }: { entry: InventoryUsage }) {
  const [urls, setUrls] = useState<string[]>([]);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    const created: string[] = [];
    Promise.all(entry.photoIds.map(id => inventoryPhotoUrl(adminInventory.photoPath(entry.id, id))))
      .then(list => {
        if (!active) { list.forEach(url => URL.revokeObjectURL(url)); return; }
        created.push(...list);
        setUrls(list);
      })
      .catch(() => { if (active) setFailed(true); });
    return () => {
      active = false;
      created.forEach(url => URL.revokeObjectURL(url));
    };
  }, [entry]);

  if (entry.photoIds.length === 0) return <Text c="dimmed">No photos were attached to this entry.</Text>;
  if (failed) return <Text c="red">Photos could not be loaded.</Text>;
  if (urls.length === 0) return <Loader size="sm" />;
  return (
    <Group>
      {urls.map((url, index) => (
        <img key={url} src={url} alt={`${entry.itemName} photo ${index + 1}`} style={{ maxWidth: '100%', borderRadius: 8 }} />
      ))}
    </Group>
  );
}

type EditState = { mode: 'create' } | { mode: 'edit'; id: string; version: string; isActive: boolean; unit?: string } | null;

function LocationsTab() {
  const [rows, setRows] = useState<InventoryLocation[] | null>(null);
  const [editing, setEditing] = useState<EditState>(null);
  const [name, setName] = useState('');
  const [saving, setSaving] = useState(false);
  const [isActive, setIsActive] = useState(true);

  useEffect(() => { refresh(); }, []);

  async function refresh() {
    try { setRows(await adminInventory.locations(true)); }
    catch (err) { showError(err instanceof Error ? err.message : 'Could not load locations.'); }
  }

  function openCreate() { setName(''); setIsActive(true); setEditing({ mode: 'create' }); }
  function openEdit(row: InventoryLocation) { setName(row.name); setIsActive(row.isActive); setEditing({ mode: 'edit', id: row.id, version: row.version, isActive: row.isActive }); }

  async function save() {
    if (!editing) return;
    setSaving(true);
    try {
      if (editing.mode === 'create') await adminInventory.createLocation(name.trim());
      else await adminInventory.updateLocation(editing.id, { name: name.trim(), isActive, version: editing.version });
      showSuccess(editing.mode === 'create' ? 'Location added.' : 'Location saved.');
      setEditing(null);
      await refresh();
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Could not save the location.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <Stack>
      <Group><Button onClick={openCreate}>Add location</Button></Group>
      {rows === null && <Loader size="sm" />}
      {rows !== null && (
        <Table.ScrollContainer minWidth={420}>
          <Table striped withTableBorder>
            <Table.Thead><Table.Tr><Table.Th>Name</Table.Th><Table.Th>Status</Table.Th><Table.Th aria-label="Actions" /></Table.Tr></Table.Thead>
            <Table.Tbody>
              {rows.map(row => (
                <Table.Tr key={row.id}>
                  <Table.Td>{row.name}</Table.Td>
                  <Table.Td><Badge color={row.isActive ? 'green' : 'gray'}>{row.isActive ? 'Active' : 'Inactive'}</Badge></Table.Td>
                  <Table.Td><Button size="xs" variant="subtle" onClick={() => openEdit(row)} aria-label={`Edit ${row.name}`}>Edit</Button></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <EditModal
        title={editing?.mode === 'create' ? 'Add location' : 'Edit location'}
        opened={editing !== null}
        onClose={() => setEditing(null)}
        name={name}
        onName={setName}
        showActive={editing?.mode === 'edit'}
        isActive={isActive}
        onActive={setIsActive}
        saving={saving}
        onSave={save}
      />
    </Stack>
  );
}

function ItemsTab() {
  const [rows, setRows] = useState<InventoryItem[] | null>(null);
  const [editing, setEditing] = useState<EditState>(null);
  const [name, setName] = useState('');
  const [unit, setUnit] = useState('');
  const [isActive, setIsActive] = useState(true);
  const [saving, setSaving] = useState(false);

  useEffect(() => { refresh(); }, []);

  async function refresh() {
    try { setRows(await adminInventory.items(true)); }
    catch (err) { showError(err instanceof Error ? err.message : 'Could not load items.'); }
  }

  function openCreate() { setName(''); setUnit(''); setIsActive(true); setEditing({ mode: 'create' }); }
  function openEdit(row: InventoryItem) {
    setName(row.name); setUnit(row.unit); setIsActive(row.isActive);
    setEditing({ mode: 'edit', id: row.id, version: row.version, isActive: row.isActive, unit: row.unit });
  }

  async function save() {
    if (!editing) return;
    setSaving(true);
    try {
      if (editing.mode === 'create') await adminInventory.createItem(name.trim(), unit.trim());
      else await adminInventory.updateItem(editing.id, { name: name.trim(), unit: unit.trim() || null, isActive, version: editing.version });
      showSuccess(editing.mode === 'create' ? 'Item added.' : 'Item saved.');
      setEditing(null);
      await refresh();
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Could not save the item.');
    } finally {
      setSaving(false);
    }
  }

  async function verify(row: InventoryItem) {
    try {
      await adminInventory.verifyItem(row.id, row.version);
      showSuccess(`${row.name} verified.`);
      await refresh();
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Could not verify the item.');
    }
  }

  return (
    <Stack>
      <Group><Button onClick={openCreate}>Add item</Button></Group>
      {rows === null && <Loader size="sm" />}
      {rows !== null && (
        <Table.ScrollContainer minWidth={520}>
          <Table striped withTableBorder>
            <Table.Thead><Table.Tr><Table.Th>Name</Table.Th><Table.Th>Unit</Table.Th><Table.Th>Status</Table.Th><Table.Th aria-label="Actions" /></Table.Tr></Table.Thead>
            <Table.Tbody>
              {rows.map(row => (
                <Table.Tr key={row.id}>
                  <Table.Td>{row.name}</Table.Td>
                  <Table.Td>{row.unit}</Table.Td>
                  <Table.Td>
                    <Group gap={6}>
                      <Badge color={row.isActive ? 'green' : 'gray'}>{row.isActive ? 'Active' : 'Inactive'}</Badge>
                      {!row.isVerified && <Badge color="yellow">Needs review</Badge>}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      {!row.isVerified && <Button size="xs" variant="light" onClick={() => verify(row)} aria-label={`Verify ${row.name}`}>Verify</Button>}
                      <Button size="xs" variant="subtle" onClick={() => openEdit(row)} aria-label={`Edit ${row.name}`}>Edit</Button>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <EditModal
        title={editing?.mode === 'create' ? 'Add item' : 'Edit item'}
        opened={editing !== null}
        onClose={() => setEditing(null)}
        name={name}
        onName={setName}
        unit={unit}
        onUnit={setUnit}
        showActive={editing?.mode === 'edit'}
        isActive={isActive}
        onActive={setIsActive}
        saving={saving}
        onSave={save}
      />
    </Stack>
  );
}

function EditModal(props: {
  title: string; opened: boolean; onClose: () => void;
  name: string; onName: (value: string) => void;
  unit?: string; onUnit?: (value: string) => void;
  showActive: boolean; isActive: boolean; onActive: (value: boolean) => void;
  saving: boolean; onSave: () => void;
}) {
  const nameValid = props.name.trim().length >= 2 && props.name.trim().length <= 80;
  return (
    <Modal opened={props.opened} onClose={props.onClose} title={props.title}>
      <Stack>
        <TextInput label="Name" value={props.name} onChange={e => props.onName(e.currentTarget.value)} maxLength={80} required />
        {props.onUnit && <TextInput label="Unit" placeholder="pcs" value={props.unit ?? ''} onChange={e => props.onUnit?.(e.currentTarget.value)} maxLength={20} />}
        {props.showActive && <Switch label="Active" checked={props.isActive} onChange={e => props.onActive(e.currentTarget.checked)} />}
        <Group justify="flex-end">
          <Button variant="subtle" onClick={props.onClose}>Cancel</Button>
          <Button onClick={props.onSave} loading={props.saving} disabled={!nameValid}>Save</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

export default function InventoryAdminPage() {
  return (
    <Stack>
      <Title order={2}>Inventory</Title>
      <Tabs defaultValue="log" keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="log">Take log</Tabs.Tab>
          <Tabs.Tab value="locations">Locations</Tabs.Tab>
          <Tabs.Tab value="items">Items</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="log" pt="md"><LogTab /></Tabs.Panel>
        <Tabs.Panel value="locations" pt="md"><LocationsTab /></Tabs.Panel>
        <Tabs.Panel value="items" pt="md"><ItemsTab /></Tabs.Panel>
      </Tabs>
    </Stack>
  );
}
