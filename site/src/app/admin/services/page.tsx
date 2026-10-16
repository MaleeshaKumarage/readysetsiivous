'use client';

import { useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Textarea, Group, Stack, Text, Loader, Badge, NumberInput } from '@mantine/core';
import { Plus } from 'lucide-react';
import { adminServices } from '@/lib/adminApi';
import { setServiceActivity } from '@/lib/serviceLifecycleApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';

type ServiceRowLocal = { id: string; slug: string; category: string; name: { values: Record<string, string> }; description: { values: Record<string, string> }; icon: string; durationMinutes: number; priceNet: number; vatRatePercent: number; isActive: boolean; isFeatured: boolean; sortOrder: number };

function localized(fi: string, en: string, sv: string): Record<string, string> {
  const d: Record<string, string> = { fi: fi.trim() };
  if (en.trim()) d.en = en.trim();
  if (sv.trim()) d.sv = sv.trim();
  return d;
}

const EMPTY = { slug: '', nameFi: '', nameEn: '', nameSv: '', descFi: '', descEn: '', descSv: '', priceNet: '', vatRatePercent: '', durationMinutes: '', category: 'cleaning' };

export default function ServicesAdminPage() {
  const [services, setServices] = useState<ServiceRowLocal[]>([]);
  const [status, setStatus] = useState<LifecycleStatus>('active');
  const visibleServices = services.filter(service => inLifecycle(service, status));
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingIsActive, setEditingIsActive] = useState(true);
  const [form, setForm] = useState(EMPTY);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [activityError, setActivityError] = useState('');
  const [activityBusy, setActivityBusy] = useState<string | null>(null);

  const load = () => adminServices.list(true).then((r) => { if (r) setServices(r as ServiceRowLocal[]); setLoading(false); });
  useEffect(() => { load(); }, []);

  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  const openAdd = () => {
    setEditingId(null);
    setEditingIsActive(true);
    setForm(EMPTY);
    setError('');
    setModalOpen(true);
  };

  const openEdit = (s: ServiceRowLocal) => {
    setEditingId(s.id);
    setEditingIsActive(s.isActive);
    setForm({
      slug: s.slug,
      nameFi: s.name?.values?.fi ?? '',
      nameEn: s.name?.values?.en ?? '',
      nameSv: s.name?.values?.sv ?? '',
      descFi: s.description?.values?.fi ?? '',
      descEn: s.description?.values?.en ?? '',
      descSv: s.description?.values?.sv ?? '',
      priceNet: String(s.priceNet ?? ''),
      vatRatePercent: String(s.vatRatePercent ?? ''),
      durationMinutes: String(s.durationMinutes ?? ''),
      category: s.category ?? 'cleaning',
    });
    setError('');
    setModalOpen(true);
  };

  const submit = async () => {
    if (!form.slug.trim() || !form.nameFi.trim()) { setError('Slug and Finnish name are required.'); return; }
    setSaving(true); setError('');
    const fields = {
      slug: form.slug.trim(),
      category: form.category,
      name: localized(form.nameFi, form.nameEn, form.nameSv),
      description: localized(form.descFi, form.descEn, form.descSv),
      additionalInfo: null,
      durationMinutes: Math.max(15, Number(form.durationMinutes) || 60),
      priceNet: Math.max(0, Number(form.priceNet) || 0),
      vatRatePercent: Math.max(0, Number(form.vatRatePercent) || 25.5),
      currency: 'EUR',
      icon: 'Sparkles',
      sortOrder: 0,
      isFeatured: false,
    };
    const ok = editingId
      ? await adminServices.update(editingId, fields, editingIsActive)
      : await adminServices.create(fields);
    setSaving(false);
    if (!ok) { setError(editingId ? 'Failed to update service.' : 'Failed to create service.'); return; }
    if (!editingId) setStatus('active');
    setForm(EMPTY);
    setEditingId(null);
    setModalOpen(false); await load();
  };

  const toggleStatus = async (s: ServiceRowLocal) => {
    setActivityBusy(s.id); setActivityError('');
    try {
      await setServiceActivity(s.id, !s.isActive);
      await load();
    } catch (err) {
      setActivityError(err instanceof Error ? err.message : 'Could not change service activity. Please retry.');
    } finally {
      setActivityBusy(null);
    }
  };

  const deleteService = async (service: ServiceRowLocal) => {
    if (!window.confirm(`Permanently delete inactive service “${service.name?.values?.fi ?? service.slug}”? Services used by bookings are retained.`)) return;
    setActivityBusy(service.id); setActivityError('');
    try { await adminServices.deleteInactive(service.id); await load(); }
    catch (failure) { setActivityError(failure instanceof Error ? failure.message : 'Could not delete service. Please retry.'); }
    finally { setActivityBusy(null); }
  };

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2}>Services</Title>
        <Button leftSection={<Plus size={16} />} onClick={openAdd}>Add service</Button>
      </Group>

      {activityError && <Text role="alert" c="red" mb="sm">{activityError}</Text>}

      <LifecycleTabs value={status} onChange={setStatus} label="Service activity"
        activeCount={loading ? undefined : services.filter(s => s.isActive).length}
        inactiveCount={loading ? undefined : services.filter(s => !s.isActive).length}>
      {loading ? <Loader /> : visibleServices.length === 0 ? (
        <Text c="dimmed">{services.length === 0 ? 'No services yet.' : `No ${status} services.`}</Text>
      ) : (
        <Table.ScrollContainer minWidth={550}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Price</Table.Th><Table.Th>Duration</Table.Th><Table.Th>Status</Table.Th><Table.Th w={120}></Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleServices.map((s) => (
                <Table.Tr key={s.id}>
                  <Table.Td fw={500}>{s.name?.values?.fi ?? s.name?.values?.en ?? ''}</Table.Td>
                  <Table.Td>{s.priceNet?.toFixed(2)} €</Table.Td>
                  <Table.Td>{s.durationMinutes} min</Table.Td>
                  <Table.Td>
                    {s.isActive
                      ? <Badge color="green">Active</Badge>
                      : <Badge color="gray">Inactive</Badge>}
                  </Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="nowrap">
                      <Button size="xs" variant="light" onClick={() => openEdit(s)}>Edit</Button>
                      <Button
                        size="xs"
                        variant="subtle"
                        color={s.isActive ? 'red' : 'green'}
                        loading={activityBusy === s.id}
                        disabled={activityBusy !== null && activityBusy !== s.id}
                        onClick={() => toggleStatus(s)}
                      >
                        {s.isActive ? 'Deactivate' : 'Reactivate'}
                      </Button>
                      {!s.isActive && <Button size="xs" variant="subtle" color="red" loading={activityBusy === s.id} disabled={activityBusy !== null && activityBusy !== s.id} onClick={() => deleteService(s)}>Delete</Button>}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      </LifecycleTabs>

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title={editingId ? 'Edit service' : 'Add service'} fullScreen={false} radius="md">
        <Stack gap="sm">
          <TextInput label="Slug" required value={form.slug} onChange={set('slug')} />

          <Group grow gap="xs">
            <TextInput label="Name (Finnish)" required value={form.nameFi} onChange={set('nameFi')} />
            <TextInput label="Name (English)" value={form.nameEn} onChange={set('nameEn')} />
            <TextInput label="Name (Swedish)" value={form.nameSv} onChange={set('nameSv')} />
          </Group>

          <Textarea label="Description (Finnish)" value={form.descFi} onChange={set('descFi')} />
          <Textarea label="Description (English)" value={form.descEn} onChange={set('descEn')} />
          <Textarea label="Description (Swedish)" value={form.descSv} onChange={set('descSv')} />

          <Group wrap="wrap" grow gap="xs">
            <NumberInput label="Price (€)" value={form.priceNet} onChange={(v) => setForm((f) => ({ ...f, priceNet: String(v ?? '') }))} />
            <NumberInput label="VAT %" value={form.vatRatePercent} onChange={(v) => setForm((f) => ({ ...f, vatRatePercent: String(v ?? '') }))} />
            <NumberInput label="Duration (min)" value={form.durationMinutes} onChange={(v) => setForm((f) => ({ ...f, durationMinutes: String(v ?? '') }))} />
          </Group>
          {error && <Text size="sm" c="red">{error}</Text>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>{editingId ? 'Save changes' : 'Create service'}</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
