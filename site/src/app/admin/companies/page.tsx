'use client';

import { useEffect, useState } from 'react';
import {
  Title, Button, Table, Modal, TextInput, Textarea, Group, Stack, Text, Loader, Badge,
} from '@mantine/core';
import { Plus, ChevronDown } from 'lucide-react';
import { adminCompanies, adminBranches, type Company, type Branch } from '@/lib/adminApi';

const emptyForm = { businessId: '', name: '', contactName: '', contactEmail: '', contactPhone: '', notes: '' };

export default function CompaniesAdminPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState(emptyForm);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const [expanded, setExpanded] = useState<string | null>(null);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [branchModal, setBranchModal] = useState(false);
  const [branchForm, setBranchForm] = useState({ name: '', street: '', postalCode: '', city: '', country: '', contactPhone: '' });
  const [branchSaving, setBranchSaving] = useState(false);

  const load = () =>
    adminCompanies.list('', 0, 100).then((r) => {
      if (r) setCompanies(r.items);
      setLoading(false);
    });

  useEffect(() => { load(); }, []);

  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));
  const setB = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setBranchForm((f) => ({ ...f, [k]: e.target.value }));

  const submit = async () => {
    if (!form.businessId.trim() || !form.name.trim()) { setError('Business ID and name are required.'); return; }
    setSaving(true); setError('');
    const created = await adminCompanies.create({
      businessId: form.businessId.trim(), name: form.name.trim(),
      contactName: form.contactName.trim() || undefined, contactEmail: form.contactEmail.trim() || undefined,
      contactPhone: form.contactPhone.trim() || undefined, notes: form.notes.trim() || undefined,
    });
    setSaving(false);
    if (!created) { setError('Failed to create company.'); return; }
    setForm(emptyForm); setModalOpen(false); await load();
  };

  const toggleBranches = async (id: string) => {
    if (expanded === id) { setExpanded(null); return; }
    setExpanded(id);
    const d = await adminCompanies.get(id);
    if (d) setBranches(d.branches);
  };

  const submitBranch = async () => {
    if (!expanded || !branchForm.name.trim()) return;
    setBranchSaving(true);
    const created = await adminBranches.create(expanded, {
      name: branchForm.name.trim(), street: branchForm.street.trim() || undefined,
      postalCode: branchForm.postalCode.trim() || undefined, city: branchForm.city.trim() || undefined,
      country: branchForm.country.trim() || undefined, contactPhone: branchForm.contactPhone.trim() || undefined,
    });
    setBranchSaving(false);
    if (!created) return;
    setBranchForm({ name: '', street: '', postalCode: '', city: '', country: '', contactPhone: '' });
    setBranchModal(false);
    const d = await adminCompanies.get(expanded);
    if (d) setBranches(d.branches);
  };

  return (
    <>
      <Group justify="space-between" mb="md">
        <Title order={2}>Companies</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => setModalOpen(true)}>Add company</Button>
      </Group>

      {loading ? (
        <Loader />
      ) : companies.length === 0 ? (
        <Text c="dimmed">No companies yet. Add one to get started.</Text>
      ) : (
        <Table striped highlightOnHover withTableBorder>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Business ID</Table.Th>
              <Table.Th>Contact</Table.Th>
              <Table.Th w={120}>Branches</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {companies.map((c) => (
              <>
                <Table.Tr key={c.id}>
                  <Table.Td fw={500}>{c.name}</Table.Td>
                  <Table.Td>{c.businessId}</Table.Td>
                  <Table.Td>{c.contactName || '—'}</Table.Td>
                  <Table.Td>
                    <Button variant="subtle" size="xs" onClick={() => toggleBranches(c.id)}>
                      {branches.length} <ChevronDown size={14} />
                    </Button>
                  </Table.Td>
                </Table.Tr>
                {expanded === c.id && (
                  <Table.Tr key={c.id + '-branches'}>
                    <Table.Td colSpan={4} p="sm">
                      <Group justify="space-between" mb="xs">
                        <Text size="sm" fw={600}>Branches</Text>
                        <Button size="xs" variant="light" leftSection={<Plus size={14} />} onClick={() => setBranchModal(true)}>Add branch</Button>
                      </Group>
                      {branches.length === 0 ? (
                        <Text size="sm" c="dimmed">No branches yet.</Text>
                      ) : (
                        <Stack gap={4}>
                          {branches.map((b) => (
                            <Group key={b.id} justify="space-between">
                              <Text size="sm">{b.name}{b.city ? ` · ${b.city}` : ''}</Text>
                              {!b.isActive && <Badge color="gray" size="xs">Inactive</Badge>}
                            </Group>
                          ))}
                        </Stack>
                      )}
                    </Table.Td>
                  </Table.Tr>
                )}
              </>
            ))}
          </Table.Tbody>
        </Table>
      )}

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title="Add company">
        <Stack>
          <TextInput label="Business ID" required value={form.businessId} onChange={set('businessId')} />
          <TextInput label="Name" required value={form.name} onChange={set('name')} />
          <TextInput label="Contact name" value={form.contactName} onChange={set('contactName')} />
          <TextInput label="Contact email" value={form.contactEmail} onChange={set('contactEmail')} />
          <TextInput label="Contact phone" value={form.contactPhone} onChange={set('contactPhone')} />
          <Textarea label="Notes" value={form.notes} onChange={set('notes')} />
          {error && <Text size="sm" c="red">{error}</Text>}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>Create company</Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={branchModal} onClose={() => setBranchModal(false)} title="Add branch">
        <Stack>
          <TextInput label="Name" required value={branchForm.name} onChange={setB('name')} />
          <TextInput label="Street" value={branchForm.street} onChange={setB('street')} />
          <TextInput label="Postal code" value={branchForm.postalCode} onChange={setB('postalCode')} />
          <TextInput label="City" value={branchForm.city} onChange={setB('city')} />
          <TextInput label="Country" value={branchForm.country} onChange={setB('country')} />
          <TextInput label="Phone" value={branchForm.contactPhone} onChange={setB('contactPhone')} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setBranchModal(false)}>Cancel</Button>
            <Button loading={branchSaving} onClick={submitBranch}>Add branch</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
