'use client';

import { DsGrid } from '@/components/ds/DsGrid';
import { useEffect, useRef, useState } from 'react';
import { Title, Tabs, Table, Button, Modal, TextInput, Select, Group, Stack, Text, Loader, Badge, Switch, Checkbox, Alert } from '@mantine/core';
import { inventoryAcknowledgments, type InventoryAcknowledgment } from '@/lib/inventoryAcknowledgmentApi';
import { AdminInventoryAcknowledgmentRequest } from '@/components/AdminInventoryAcknowledgmentRequest';
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
  const [selected, setSelected] = useState<string[]>([]);
  const [requesting, setRequesting] = useState(false);
  const [acknowledgments, setAcknowledgments] = useState<Record<string, InventoryAcknowledgment>>({});
  const [ackError, setAckError] = useState('');
  const [responseView, setResponseView] = useState<InventoryAcknowledgment | null>(null);

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
      // Load more appends rows, so keep the selection; only a fresh load clears it.
      if (skip === 0) setSelected([]);
      try {
        const statuses = page.length ? await inventoryAcknowledgments.lookup(page.map(row => row.id)) : [];
        if (request !== latestRequest.current) return;
        const mapped = Object.fromEntries(statuses.map(row => [row.id, row]));
        setAcknowledgments(previous => skip === 0 ? mapped : { ...previous, ...mapped });
        setAckError('');
      } catch {
        if (request !== latestRequest.current) return;
        setAckError('Acknowledgment statuses could not be loaded. Reload the log before requesting acknowledgment.');
      }
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
      <Text size="sm" c="dimmed">Select up to 50 entries with available photos to request partner acknowledgment. Responses do not change the inventory log.</Text>
      {ackError && <Alert color="red">{ackError}<Button variant="subtle" onClick={() => load(applied, 0)}>Reload log</Button></Alert>}
      <Group><Button style={{ minHeight: 44 }} disabled={!selected.length || loading || !!ackError} onClick={() => setRequesting(true)}>Request acknowledgment ({selected.length})</Button>
        {selected.length > 0 && <Button variant="subtle" onClick={() => setSelected([])}>Clear selection</Button>}
      </Group>
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
        <DsGrid<(typeof rows)[number]>
          label="Inventory takes"
          rows={rows}
          getRowId={(row) => row.id}
          columns={[
            { headerName: 'Select', colId: 'select', sortable: false, width: 90, flex: 0, cellRenderer: (p: { data?: (typeof rows)[number] }) => p.data ? (
              <Checkbox styles={{ root: { minHeight: 44, display: 'flex', alignItems: 'center' } }} aria-label={`Select ${p.data.itemName} by ${p.data.employeeName}`} checked={selected.includes(p.data.id)}
                disabled={loading || !!ackError || !!acknowledgments[p.data.id] || p.data.photosPurged || p.data.photoIds.length === 0 || (!selected.includes(p.data.id) && selected.length >= 50)}
                onChange={event => { const checked = event.currentTarget.checked; const id = p.data!.id; setSelected(previous => checked ? [...previous, id] : previous.filter(x => x !== id)); }} />
            ) : null },
            { headerName: 'Taken', colId: 'taken', valueGetter: p => p.data ? takenLabel(p.data.takenUtc) : '' },
            { headerName: 'Employee', colId: 'employee', valueGetter: p => p.data?.employeeName ?? '' },
            { headerName: 'Location', colId: 'location', valueGetter: p => p.data?.locationName ?? '' },
            { headerName: 'Item', colId: 'item', valueGetter: p => p.data?.itemName ?? '' },
            { headerName: 'Quantity', colId: 'quantity', valueGetter: p => p.data ? `${p.data.quantity} ${p.data.unit}` : '' },
            { headerName: 'Note', colId: 'note', flex: 2, valueGetter: p => p.data?.note ?? '' },
            { headerName: 'Photos', colId: 'photos', sortable: false, cellRenderer: (p: { data?: (typeof rows)[number] }) => p.data ? (
              p.data.photosPurged ? <Text size="sm" c="dimmed">Deleted</Text> : (
                <Button size="xs" variant="subtle" onClick={() => setPhotoView(p.data!)}>View {p.data.photoIds.length}</Button>
              )
            ) : null },
            { headerName: 'Acknowledgment', colId: 'ack', sortable: false, flex: 2, cellRenderer: (p: { data?: (typeof rows)[number] }) => {
              if (!p.data) return null;
              const row = p.data;
              return ackError ? <Text size="sm" c="dimmed">Status unavailable</Text> : acknowledgments[row.id] ? <Button variant="subtle" size="xs" onClick={() => setResponseView(acknowledgments[row.id])}>
                {acknowledgments[row.id].status === 'Pending' ? 'Awaiting acknowledgment' : acknowledgments[row.id].status === 'Acknowledged' ? 'Acknowledged' : 'Issue reported'}
              </Button> : <Text size="sm" c="dimmed">Not requested</Text>;
            } },
          ]}
        />
      )}
      {hasMore && (
        <Group>
          <Button variant="light" loading={loading} onClick={() => load(applied, rows?.length ?? 0)}>Load more</Button>
        </Group>
      )}

      <Modal opened={photoView !== null} onClose={() => setPhotoView(null)} title={photoView ? `${photoView.itemName}, ${photoView.employeeName}` : ''} size="lg">
        {photoView && <PhotoGallery entry={photoView} />}
      </Modal>
      {requesting && rows && <AdminInventoryAcknowledgmentRequest rows={rows.filter(row => selected.includes(row.id))}
        onClose={() => setRequesting(false)} onSent={sent => {
          setAcknowledgments(previous => ({ ...previous, ...Object.fromEntries(sent.map(row => [row.id, row])) }));
          setSelected([]); setRequesting(false); showSuccess('Acknowledgment request sent to the partner portal.');
        }} />}
      <Modal opened={responseView !== null} onClose={() => setResponseView(null)} title="Partner acknowledgment">
        {responseView && <Stack><Text fw={600}>{responseView.itemName} · {responseView.companyName}</Text>
          <Text>{responseView.status === 'Pending' ? 'Awaiting acknowledgment' : responseView.status === 'Acknowledged' ? 'Acknowledged' : 'Issue reported'}</Text>
          <Text>Requested {takenLabel(responseView.createdUtc)}</Text>
          {responseView.message && <Text style={{ whiteSpace: 'pre-wrap' }}>Message: {responseView.message}</Text>}
          {responseView.respondedUtc && <Text>{responseView.respondedByName} · {takenLabel(responseView.respondedUtc)}</Text>}
          {responseView.responseNote && <Text style={{ whiteSpace: 'pre-wrap' }}>Response: {responseView.responseNote}</Text>}
        </Stack>}
      </Modal>
    </Stack>
  );
}

type GalleryState = { status: 'loading' | 'ready' | 'failed'; urls: string[] };

function PhotoGallery({ entry }: { entry: InventoryUsage }) {
  const [gallery, setGallery] = useState<GalleryState>({ status: 'loading', urls: [] });

  useEffect(() => {
    // Reset on every entry change, so one failed load does not stick to the entries shown after it.
    let active = true;
    const created: string[] = [];
    setGallery({ status: 'loading', urls: [] });
    Promise.all(entry.photoIds.map(id => inventoryPhotoUrl(adminInventory.photoPath(entry.id, id))))
      .then(list => {
        if (!active) { list.forEach(url => URL.revokeObjectURL(url)); return; }
        created.push(...list);
        setGallery({ status: 'ready', urls: list });
      })
      .catch(() => { if (active) setGallery({ status: 'failed', urls: [] }); });
    return () => {
      active = false;
      created.forEach(url => URL.revokeObjectURL(url));
    };
  }, [entry]);

  if (entry.photoIds.length === 0) return <Text c="dimmed">No photos were attached to this entry.</Text>;
  if (gallery.status === 'failed') return <Text c="red">Photos could not be loaded.</Text>;
  if (gallery.status === 'loading') return <Loader size="sm" />;
  const urls = gallery.urls;
  if (urls.length === 0) return <Text c="dimmed">No photos could be shown for this entry.</Text>;
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
        <DsGrid<(typeof rows)[number]>
          label="Inventory items"
          rows={rows}
          getRowId={(row) => row.id}
          columns={[
            { headerName: 'Name', colId: 'name', flex: 2, valueGetter: p => p.data?.name ?? '' },
            { headerName: 'Status', colId: 'status', sortable: false, cellRenderer: (p: { data?: (typeof rows)[number] }) => p.data ? <Badge color={p.data.isActive ? 'green' : 'gray'}>{p.data.isActive ? 'Active' : 'Inactive'}</Badge> : null },
            { headerName: '', colId: 'actions', sortable: false, width: 120, flex: 0, cellRenderer: (p: { data?: (typeof rows)[number] }) => p.data ? <Button size="xs" variant="subtle" onClick={() => openEdit(p.data!)} aria-label={`Edit ${p.data.name}`}>Edit</Button> : null },
          ]}
        />
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
        <DsGrid<(typeof rows)[number]>
          label="Inventory items with units"
          rows={rows}
          getRowId={(row) => row.id}
          columns={[
            { headerName: 'Name', colId: 'name', flex: 2, valueGetter: p => p.data?.name ?? '' },
            { headerName: 'Unit', colId: 'unit', valueGetter: p => p.data?.unit ?? '' },
            { headerName: 'Status', colId: 'status', sortable: false, cellRenderer: (p: { data?: (typeof rows)[number] }) => p.data ? (
              <div className="flex flex-wrap items-center gap-1.5"><Badge color={p.data.isActive ? 'green' : 'gray'}>{p.data.isActive ? 'Active' : 'Inactive'}</Badge>{!p.data.isVerified && <Badge color="yellow">Needs review</Badge>}</div>
            ) : null },
            { headerName: '', colId: 'actions', sortable: false, minWidth: 200, flex: 0, width: 220, cellRenderer: (p: { data?: (typeof rows)[number] }) => p.data ? (
              <div className="flex items-center gap-1">{!p.data.isVerified && <Button size="xs" variant="light" onClick={() => verify(p.data!)} aria-label={`Verify ${p.data.name}`}>Verify</Button>}<Button size="xs" variant="subtle" onClick={() => openEdit(p.data!)} aria-label={`Edit ${p.data.name}`}>Edit</Button></div>
            ) : null },
          ]}
        />
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
