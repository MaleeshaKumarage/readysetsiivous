'use client';

import { useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Textarea, Group, Stack, Text, Loader, Badge, NumberInput, Select } from '@mantine/core';
import { Plus } from 'lucide-react';
import { adminServices } from '@/lib/adminApi';

type ServiceRowLocal = { id: string; slug: string; category: string; name: { values: Record<string, string> }; description: { values: Record<string, string> }; icon: string; durationMinutes: number; priceNet: number; vatRatePercent: number; isActive: boolean; isFeatured: boolean; sortOrder: number };

export default function ServicesAdminPage() {
  const [services, setServices] = useState<ServiceRowLocal[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState({ slug: '', name: '', description: '', priceNet: '', vatRatePercent: '', durationMinutes: '', category: 'cleaning' });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = () => adminServices.list(true).then((r) => { if (r) setServices(r as ServiceRowLocal[]); setLoading(false); });
  useEffect(() => { load(); }, []);

  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  const submit = async () => {
    if (!form.slug.trim() || !form.name.trim()) { setError('Slug and name are required.'); return; }
    setSaving(true); setError('');
    const created = await adminServices.create({
      slug: form.slug.trim(),
      category: form.category,
      name: { values: { fi: form.name.trim() } },
      description: { values: { fi: form.description.trim() } },
      durationMinutes: Number(form.durationMinutes) || 0,
      priceNet: Number(form.priceNet) || 0,
      vatRatePercent: Number(form.vatRatePercent) || 0,
      icon: 'Sparkles',
      sortOrder: 0,
      isActive: true,
      isFeatured: false,
    });
    setSaving(false);
    if (!created) { setError('Failed to create service.'); return; }
    setForm({ slug: '', name: '', description: '', priceNet: '', vatRatePercent: '', durationMinutes: '', category: 'cleaning' });
    setModalOpen(false); await load();
  };

  const deactivate = async (id: string) => {
    const s = services.find((x) => x.id === id);
    if (s) { await adminServices.update(id, { isActive: false }, false); await load(); }
  };

  return (
    <>
      <Group justify="space-between" mb="md">
        <Title order={2}>Services</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => setModalOpen(true)}>Add service</Button>
      </Group>

      {loading ? <Loader /> : services.length === 0 ? (
        <Text c="dimmed">No services yet.</Text>
      ) : (
        <Table striped highlightOnHover withTableBorder>
          <Table.Thead>
            <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Price</Table.Th><Table.Th>Duration</Table.Th><Table.Th>Status</Table.Th></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {services.map((s) => (
              <Table.Tr key={s.id}>
                <Table.Td fw={500}>{s.name.values.fi ?? s.name.values.en ?? ''}</Table.Td>
                <Table.Td>{s.priceNet.toFixed(2)} €</Table.Td>
                <Table.Td>{s.durationMinutes} min</Table.Td>
                <Table.Td>
                  {s.isActive
                    ? <Badge color="green">Active</Badge>
                    : <Badge color="gray">Inactive</Badge>}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title="Add service">
        <Stack>
          <TextInput label="Slug" required value={form.slug} onChange={set('slug')} />
          <TextInput label="Name (fi)" required value={form.name} onChange={set('name')} />
          <Textarea label="Description (fi)" value={form.description} onChange={set('description')} />
          <Group grow>
            <NumberInput label="Price (€)" value={form.priceNet} onChange={(v) => setForm((f) => ({ ...f, priceNet: String(v ?? '') }))} />
            <NumberInput label="VAT %" value={form.vatRatePercent} onChange={(v) => setForm((f) => ({ ...f, vatRatePercent: String(v ?? '') }))} />
            <NumberInput label="Duration (min)" value={form.durationMinutes} onChange={(v) => setForm((f) => ({ ...f, durationMinutes: String(v ?? '') }))} />
          </Group>
          {error && <Text size="sm" c="red">{error}</Text>}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>Create service</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
