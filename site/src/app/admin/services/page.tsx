'use client';

import { useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Textarea, Group, Stack, Text, Loader, Badge, NumberInput } from '@mantine/core';
import { Plus } from 'lucide-react';
import { adminServices } from '@/lib/adminApi';

type ServiceRowLocal = { id: string; slug: string; category: string; name: { values: Record<string, string> }; description: { values: Record<string, string> }; icon: string; durationMinutes: number; priceNet: number; vatRatePercent: number; isActive: boolean; isFeatured: boolean; sortOrder: number };

export default function ServicesAdminPage() {
  const [services, setServices] = useState<ServiceRowLocal[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingIsActive, setEditingIsActive] = useState(true);
  const [form, setForm] = useState({ slug: '', name: '', description: '', priceNet: '', vatRatePercent: '', durationMinutes: '', category: 'cleaning' });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = () => adminServices.list(true).then((r) => { if (r) setServices(r as ServiceRowLocal[]); setLoading(false); });
  useEffect(() => { load(); }, []);

  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  const openAdd = () => {
    setEditingId(null);
    setEditingIsActive(true);
    setForm({ slug: '', name: '', description: '', priceNet: '', vatRatePercent: '', durationMinutes: '', category: 'cleaning' });
    setError('');
    setModalOpen(true);
  };

  const openEdit = (s: ServiceRowLocal) => {
    setEditingId(s.id);
    setEditingIsActive(s.isActive);
    setForm({
      slug: s.slug,
      name: s.name?.values?.fi ?? s.name?.values?.en ?? '',
      description: s.description?.values?.fi ?? s.description?.values?.en ?? '',
      priceNet: String(s.priceNet ?? ''),
      vatRatePercent: String(s.vatRatePercent ?? ''),
      durationMinutes: String(s.durationMinutes ?? ''),
      category: s.category ?? 'cleaning',
    });
    setError('');
    setModalOpen(true);
  };

  const submit = async () => {
    if (!form.slug.trim() || !form.name.trim()) { setError('Slug and name are required.'); return; }
    setSaving(true); setError('');
    const fields = {
      slug: form.slug.trim(),
      category: form.category,
      name: { fi: form.name.trim() },
      description: { fi: form.description.trim() },
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
    setForm({ slug: '', name: '', description: '', priceNet: '', vatRatePercent: '', durationMinutes: '', category: 'cleaning' });
    setEditingId(null);
    setModalOpen(false); await load();
  };

  const toggleStatus = async (s: ServiceRowLocal) => {
    const fields = {
      slug: s.slug,
      category: s.category,
      name: s.name.values,
      description: s.description.values,
      additionalInfo: null,
      durationMinutes: s.durationMinutes,
      priceNet: s.priceNet,
      vatRatePercent: s.vatRatePercent,
      currency: 'EUR',
      icon: s.icon,
      sortOrder: s.sortOrder,
      isFeatured: s.isFeatured,
    };
    await adminServices.update(s.id, fields, !s.isActive);
    await load();
  };

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2}>Services</Title>
        <Button leftSection={<Plus size={16} />} onClick={openAdd}>Add service</Button>
      </Group>

      {loading ? <Loader /> : services.length === 0 ? (
        <Text c="dimmed">No services yet.</Text>
      ) : (
        <Table.ScrollContainer minWidth={550}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Price</Table.Th><Table.Th>Duration</Table.Th><Table.Th>Status</Table.Th><Table.Th w={120}></Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {services.map((s) => (
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
                        onClick={() => toggleStatus(s)}
                      >
                        {s.isActive ? 'Deactivate' : 'Activate'}
                      </Button>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title={editingId ? 'Edit service' : 'Add service'} fullScreen={false} radius="md">
        <Stack gap="sm">
          <TextInput label="Slug" required value={form.slug} onChange={set('slug')} />
          <TextInput label="Name (fi)" required value={form.name} onChange={set('name')} />
          <Textarea label="Description (fi)" value={form.description} onChange={set('description')} />
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
