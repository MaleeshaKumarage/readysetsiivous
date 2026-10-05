'use client';

import { useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Select, Group, Stack, Text, Loader, Badge } from '@mantine/core';
import { Plus } from 'lucide-react';
import { adminEmployees, type Employee } from '@/lib/adminApi';

const ROLES = ['admin', 'employee'];

export default function EmployeesAdminPage() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState({ email: '', firstName: '', lastName: '', phone: '', role: 'employee' });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = () => adminEmployees.list().then((r) => { if (r) setEmployees(r); setLoading(false); });
  useEffect(() => { load(); }, []);

  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  const submit = async () => {
    if (!form.email.trim() || !form.firstName.trim() || !form.lastName.trim()) { setError('Email, first and last name are required.'); return; }
    setSaving(true); setError('');
    const created = await adminEmployees.create({
      email: form.email.trim(), firstName: form.firstName.trim(), lastName: form.lastName.trim(),
      phone: form.phone.trim(), role: form.role,
      skills: [], serviceAreas: [], colorHex: '', payRate: undefined, notes: '', certifications: [], defaultHours: {},
    });
    setSaving(false);
    if (!created) { setError('Failed to create employee.'); return; }
    setForm({ email: '', firstName: '', lastName: '', phone: '', role: 'employee' });
    setModalOpen(false); await load();
  };

  const deactivate = async (id: string) => { await adminEmployees.deactivate(id); await load(); };

  return (
    <>
      <Group justify="space-between" mb="md">
        <Title order={2}>Employees</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => setModalOpen(true)}>Add employee</Button>
      </Group>

      {loading ? <Loader /> : employees.length === 0 ? (
        <Text c="dimmed">No employees yet.</Text>
      ) : (
        <Table striped highlightOnHover withTableBorder>
          <Table.Thead>
            <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Email</Table.Th><Table.Th>Role</Table.Th><Table.Th>Status</Table.Th><Table.Th w={120}></Table.Th></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {employees.map((e) => (
              <Table.Tr key={e.id}>
                <Table.Td fw={500}>{e.firstName} {e.lastName}</Table.Td>
                <Table.Td>{e.email}</Table.Td>
                <Table.Td>{e.role}</Table.Td>
                <Table.Td>{e.isActive ? <Badge color="green">Active</Badge> : <Badge color="gray">Inactive</Badge>}</Table.Td>
                <Table.Td>{e.isActive && <Button size="xs" variant="subtle" color="red" onClick={() => deactivate(e.id)}>Deactivate</Button>}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title="Add employee">
        <Stack>
          <TextInput label="Email" required type="email" value={form.email} onChange={set('email')} />
          <Group grow>
            <TextInput label="First name" required value={form.firstName} onChange={set('firstName')} />
            <TextInput label="Last name" required value={form.lastName} onChange={set('lastName')} />
          </Group>
          <TextInput label="Phone" value={form.phone} onChange={set('phone')} />
          <Select label="Role" data={ROLES} value={form.role} onChange={(v) => setForm((f) => ({ ...f, role: v ?? 'employee' }))} />
          {error && <Text size="sm" c="red">{error}</Text>}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>Create employee</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
